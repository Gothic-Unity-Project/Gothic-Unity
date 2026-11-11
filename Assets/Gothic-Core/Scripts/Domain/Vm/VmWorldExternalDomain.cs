using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.UI;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.Core.Models.Doc;
using Gothic.Core.Creator;
using MyBox;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Random = UnityEngine.Random;
using Time = UnityEngine.Time;

namespace Gothic.Core.Domain.Vm
{
    /// <summary>
    /// Daedalus Wld_* externals (spawning, detection, time, guild attitudes, triggers).
    /// </summary>
    public class VmWorldExternalDomain
    {
        // Wld_SpawnNpcRange: the ring around the caster in meters (OpenGothic fixNpcPosition(.., 100) cm).
        private const float _summonRingRadius = 1f;

        [Inject] private readonly Gothic.Core.Services.Meshes.ParticleService _particleService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly GameTimeService _gameTimeService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly MobRoutineService _mobRoutineService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly RoomService _roomService;
        [Inject] private readonly SkyService _skyService;

        private readonly VmExternalSupport _support;

        public VmWorldExternalDomain(VmExternalSupport support)
        {
            _support = support;
            this.Inject();
        }

        public void Register(VmExternalGuard vm)
        {
            if (vm.GetSymbolByName("Wld_InsertObject") != null)
                vm.RegisterExternal<string, string>("Wld_InsertObject", Wld_InsertObject);
            VmExternalSupport.RegisterIfDeclared(vm, "Wld_IsRaining",
                () => vm.RegisterExternal("Wld_IsRaining", Wld_IsRaining));
            // Portal rooms (RoomService), mobs in use and noises (HeroPerceptionService) - G1 and G2. Only registered
            // when the scripts declare them (registering a missing symbol throws).
            VmExternalSupport.RegisterIfDeclared(vm, "Wld_GetPlayerPortalGuild",
                () => vm.RegisterExternal<int>("Wld_GetPlayerPortalGuild", Wld_GetPlayerPortalGuild));
            VmExternalSupport.RegisterIfDeclared(vm, "Wld_GetFormerPlayerPortalGuild",
                () => vm.RegisterExternal<int>("Wld_GetFormerPlayerPortalGuild", Wld_GetFormerPlayerPortalGuild));
            VmExternalSupport.RegisterIfDeclared(vm, "Wld_RemoveNpc",
                () => vm.RegisterExternal<int>("Wld_RemoveNpc", Wld_RemoveNpc));
            if (vm.GetSymbolByName("Wld_PlayEffect") != null)
                vm.RegisterExternal<string, DaedalusInstance, DaedalusInstance, int, int, int, int>("Wld_PlayEffect",
                    Wld_PlayEffect);
            vm.RegisterExternal<int, string>("Wld_InsertNpc", Wld_InsertNpc);
            // Externals.d declares the last param as VAR FLOAT — registering it as int would hand us
            // the raw IEEE-754 bit pattern instead (500.0f => 1140457472, i.e. "11.4 million meters").
            vm.RegisterExternal<NpcInstance, int, int, float>("Wld_SpawnNpcRange", Wld_SpawnNpcRange);
            vm.RegisterExternal<int, NpcInstance, string>("Wld_IsFPAvailable", Wld_IsFPAvailable);
            vm.RegisterExternal<int, NpcInstance, string>("Wld_IsMobAvailable", Wld_IsMobAvailable);
            vm.RegisterExternal<int, NpcInstance, int, int, int>("Wld_DetectNpc", Wld_DetectNpc);
            vm.RegisterExternal<int, NpcInstance, int, int, int, int>("Wld_DetectNpcEx", Wld_DetectNpcEx);
            vm.RegisterExternal<int, NpcInstance, int>("Wld_DetectItem", Wld_DetectItem);
            vm.RegisterExternal<int, NpcInstance, string>("Wld_IsNextFPAvailable", Wld_IsNextFPAvailable);
            vm.RegisterExternal<int, int>("Wld_SetTime", Wld_SetTime);
            vm.RegisterExternal("Wld_GetDay", Wld_GetDay);
            vm.RegisterExternal<int, int, int, int, int>("Wld_IsTime", Wld_IsTime);
            vm.RegisterExternal<int, NpcInstance, string>("Wld_GetMobState", Wld_GetMobState);
            vm.RegisterExternal<int, string>("Wld_InsertItem", Wld_InsertItem);
            vm.RegisterExternal<string>("Wld_ExchangeGuildAttitudes", Wld_ExchangeGuildAttitudes);
            vm.RegisterExternal<int, int, int>("Wld_SetGuildAttitude", Wld_SetGuildAttitude);
            vm.RegisterExternal<int, int, string, int>("Wld_SetObjectRoutine", Wld_SetObjectRoutine);
            vm.RegisterExternal<int, int, string, int>("Wld_SetMobRoutine", Wld_SetMobRoutine);
            vm.RegisterExternal<string, int>("Wld_AssignRoomToGuild", Wld_AssignRoomToGuild);
            vm.RegisterExternal<int, int, int>("Wld_GetGuildAttitude", Wld_GetGuildAttitude);
            vm.RegisterExternal<string>("Wld_SendTrigger", Wld_SendTrigger);
            vm.RegisterExternal<string>("Wld_SendUntrigger", Wld_SendUntrigger);
            if (vm.GetSymbolByName("Npc_GetGuildAttitude") != null)
                vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_GetGuildAttitude",
                    (a, b) =>
                        _support.AsNpc(a, "Npc_GetGuildAttitude") is { } first &&
                            _support.AsNpc(b, "Npc_GetGuildAttitude") is { } second
                        ? Npc_GetGuildAttitude(first, second) : 0);
        }

        /// <summary>
        /// The VISUALFX at the origin NPC (hips height). Projectiles (bIsProjectile) and damage aren't simulated -
        /// the effect plays at the origin.
        /// </summary>
        public void Wld_PlayEffect(string effect, DaedalusInstance origin, DaedalusInstance target, int effectLevel,
            int damage, int damageType, int isProjectile)
        {
            var originGo = (origin as NpcInstance)?.GetUserData()?.Go;
            Logger.Log($"[Wld_PlayEffect] {effect} at {(originGo != null ? originGo.name : "-")} " +
                       $"(level {effectLevel}, " +
                       $"projectile {isProjectile})", LogCat.Vob);
            if (originGo == null)
                return;
            _particleService.PlayVisualFx(effect, originGo.transform.position, originGo.transform);
        }

        /// <summary>
        /// Attitude between the guilds of both NPCs (= Wld_GetGuildAttitude(self.guild, other.guild)).
        /// </summary>
        public int Npc_GetGuildAttitude(NpcInstance self, NpcInstance other)
        {
            if (self == null || other == null)
                return _support.LogInstantExternal(nameof(Npc_GetGuildAttitude), 0, self, other);
            var ret = Wld_GetGuildAttitude(self.Guild, other.Guild);
            return _support.LogInstantExternal(nameof(Npc_GetGuildAttitude), ret, self, other);
        }

        public void Wld_InsertNpc(int npcInstance, string spawnPoint)
        {
            _npcService.ExtWldInsertNpc(npcInstance, spawnPoint);
        }

        public void Wld_SpawnNpcRange(NpcInstance caster, int spawnInstance, int count, float lifeTime)
        {
            var container = caster.GetUserData();
            if (container?.Go == null)
            {
                Logger.LogWarning("[Wld_SpawnNpcRange] Caster has no GO", LogCat.Npc);
                return;
            }

            var casterPos = container.Go.transform.position;
            var casterYaw = container.Go.transform.eulerAngles.y;
            // Like OpenGothic (wld_spawnnpcrange, gamescript.cpp:1853-1863): the script's 4th parameter (lifeTime) is
            // ignored, the NPCs stand on a ring of 1 m around the caster at the caster's rotation + 360 * i / count.
            for (var i = 0; i < count; i++)
            {
                var yaw = casterYaw + 360f * i / count;
                var offset = UnityEngine.Quaternion.Euler(0f, yaw, 0f) * UnityEngine.Vector3.forward * _summonRingRadius;
                _npcService.SpawnNpcRuntime(spawnInstance, casterPos + offset, container.Go.transform.rotation, caster);
            }

            Logger.Log($"[Wld_SpawnNpcRange] Spawned {count}x instance={spawnInstance} on a {_summonRingRadius}m ring " +
                       "around the caster", LogCat.Npc);
        }

        public int Wld_IsFPAvailable(NpcInstance npc, string fpName)
        {
            var response = _npcService.ExtWldIsFpAvailable(npc, fpName);
            return _support.LogInstantExternal(nameof(Wld_IsFPAvailable), Convert.ToInt32(response), npc, fpName);
        }

        public int Wld_IsMobAvailable(NpcInstance npc, string vobName)
        {
            var res = _npcHelperService.ExtIsMobAvailable(npc, vobName);
            return _support.LogInstantExternal(nameof(Wld_IsMobAvailable), Convert.ToInt32(res), npc, vobName);
        }

        public int Wld_DetectNpc(NpcInstance npc, int npcInstance, int aiState, int guild)
        {
            var ret = Wld_DetectNpcEx(npc, npcInstance, aiState, guild, 1);
            return _support.LogInstantExternal(nameof(Wld_DetectNpc), ret, npc, npcInstance, aiState, guild);
        }

        public int Wld_DetectNpcEx(NpcInstance npc, int npcInstance, int aiState, int guild, int detectPlayer)
        {
            var res = _npcHelperService.ExtWldDetectNpcEx(npc, npcInstance, aiState, guild,
                Convert.ToBoolean(detectPlayer));
            var ret = Convert.ToInt32(res);
            return _support.LogInstantExternal(nameof(Wld_DetectNpcEx), ret, npc, npcInstance, aiState, guild,
                detectPlayer);
        }

        public int Wld_DetectItem(NpcInstance npc, int flags)
        {
            var result = _npcHelperService.ExtWldDetectItem(npc, flags);
            return _support.LogInstantExternal(nameof(Wld_DetectItem), Convert.ToInt32(result), npc, flags);
        }

        public int Wld_IsNextFPAvailable(NpcInstance npc, string fpNamePart)
        {
            var result = _npcService.ExtIsNextFpAvailable(npc, fpNamePart);
            return _support.LogInstantExternal(nameof(Wld_IsNextFPAvailable), Convert.ToInt32(result), npc, fpNamePart);
        }

        public void Wld_SetTime(int hour, int minute)
        {
            _gameTimeService.SetTime(hour, minute);
        }

        public int Wld_GetDay()
        {
            var ret = _gameTimeService.GetDay();
            return _support.LogInstantExternal(nameof(Wld_GetDay), ret);
        }

        /// <summary>
        /// G2's NPCs complain about the weather in their greetings while it rains.
        /// </summary>
        public int Wld_IsRaining()
        {
            var ret = Convert.ToInt32(_skyService.IsRaining);
            return _support.LogInstantExternal(nameof(Wld_IsRaining), ret);
        }

        public int Wld_IsTime(int beginHour, int beginMinute, int endHour, int endMinute)
        {
            var begin = new TimeSpan(beginHour, beginMinute, 0);
            var end = new TimeSpan(endHour, endMinute, 0);

            var now = _gameTimeService.GetCurrentTime();

            if (begin <= end && begin <= now && now < end)
            {
                return _support.LogInstantExternal(nameof(Wld_IsTime), 1, beginHour, beginMinute, endHour, endMinute);
            }

            if (begin > end && (begin < now || now <= end)) // begin and end span across midnight
            {
                return _support.LogInstantExternal(nameof(Wld_IsTime), 1, beginHour, beginMinute, endHour, endMinute);
            }

            return _support.LogInstantExternal(nameof(Wld_IsTime), 0, beginHour, beginMinute, endHour, endMinute);
        }

        public int Wld_GetMobState(NpcInstance npc, string scheme)
        {
            var ret = _npcHelperService.ExtWldGetMobState(npc, scheme);
            return _support.LogInstantExternal(nameof(Wld_GetMobState), ret, npc, scheme);
        }

        public void Wld_InsertItem(int itemInstance, string spawnpoint)
        {
            _vobService.ExtWldInsertItem(itemInstance, spawnpoint);
        }

        public int Wld_GetPlayerPortalGuild()
        {
            // GIL_NONE (0) outdoors and in rooms no script gave to a guild.
            return _roomService.GuildOfRoom(_roomService.HeroRoom);
        }

        public int Wld_GetFormerPlayerPortalGuild()
        {
            return _roomService.GuildOfRoom(_roomService.HeroFormerRoom);
        }

        public void Wld_RemoveNpc(int npcInstance)
        {
            _npcService.ExtWldRemoveNpc(npcInstance);
        }

        public void Wld_InsertObject(string visual, string spawnpoint)
        {
            _vobService.ExtWldInsertObject(visual, spawnpoint);
        }

        public void Wld_ExchangeGuildAttitudes(string name)
        {
            var humanGguildAttitudes = _gameStateService.GothicVm.GetSymbolByName(name);

            if (humanGguildAttitudes == null)
                return;

            
            // The guild attitudes are in a matrix. Where a row is the first guild and columns are the second one to
            // check against.
            // The first part of this matrix is human guilds. e.g. 16x16 based on the full matrix e.g., 42x42
            // We therefore need to loop through the humanGuilds but fill the values inside allGuilds matrix first area
            // top-left.
            var humanGuildCount = _gameStateService.GuildHumanCount;
            var allGuildCount = _gameStateService.GuildCount;
            for (var row = 0; row < humanGuildCount; ++row)
            {
                for (var column = 0; column < humanGuildCount; ++column)
                {
                    var humanAttitudeIndex = row * humanGuildCount + column;
                    var allGuildAttitudeIndex = row * allGuildCount + column;
                    _gameStateService.GuildAttitudes[allGuildAttitudeIndex] =
                        humanGguildAttitudes.GetInt((ushort)humanAttitudeIndex);
                }
            }
        }

        public void Wld_SetGuildAttitude(int guild1, int attitude, int guild2)
        {
            if (guild1 < 0 ||
                guild2 < 0 ||
                guild1 >= _gameStateService.GuildCount ||
                guild2 >= _gameStateService.GuildCount)
                return;

            _gameStateService.GuildAttitudes[guild1 * _gameStateService.GuildCount + guild2] = attitude;
        }

        public void Wld_SetObjectRoutine(int hour, int minute, string name, int status)
        {
            // FIXME - Do more with these ObjectRoutines.
            _vobService.ObjectRoutines.TryAdd(name, new());
            _vobService.ObjectRoutines[name].Add((hour, minute, status));
        }

        public void Wld_SetMobRoutine(int hour, int minute, string name, int status)
        {
            _mobRoutineService.Add(name, hour, minute, status);
        }

        public void Wld_AssignRoomToGuild(string room, int guild)
        {
            _roomService.AssignRoomToGuild(room, guild);
        }

        public int Wld_GetGuildAttitude(int guild1, int guild2)
        {
            if (guild1 < 0 ||
                guild2 < 0 ||
                guild1 >= _gameStateService.GuildCount ||
                guild2 >= _gameStateService.GuildCount)
                return _support.LogInstantExternal(nameof(Wld_GetGuildAttitude), 0, guild1, guild2);

            var ret = _gameStateService.GuildAttitudes[guild1 * _gameStateService.GuildCount + guild2];
            return _support.LogInstantExternal(nameof(Wld_GetGuildAttitude), ret, guild1, guild2);
        }

        public void Wld_SendTrigger(string vobName)
        {
            _vobService.DispatchTrigger(vobName);
        }

        public void Wld_SendUntrigger(string vobName)
        {
            _vobService.DispatchUntrigger(vobName);
        }
    }
}
