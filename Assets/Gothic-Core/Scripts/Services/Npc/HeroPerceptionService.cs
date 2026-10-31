using System;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// What the engine tells the NPCs about the hero on its own (OpenGothic: Npc/MoveAlgo/Interactive):
    /// - PERC_ASSESSUSEMOB when the hero uses a mob (opens a chest or door, tries a lock),
    /// - PERC_ASSESSENTERROOM when he walks into another portal room (RoomService).
    /// The Daedalus scripts decide what the NPCs do with it (owner's chest, whose hut, ...).
    /// </summary>
    public class HeroPerceptionService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly RoomService _roomService;

        private const float _roomCheckInterval = 0.25f;
        // A new room counts after this many checks in a row - standing on a doorstep flipped in and out.
        private const int _roomStableChecks = 2;
        private const float _sameMobRepeatSeconds = 2f;

        private float _nextRoomCheckTime;
        private string _pendingRoom;
        private int _pendingRoomChecks;
        private float _lastUseMobTime;
        private IInteractiveObject _lastUseMob;

        /// <summary>
        /// Npc_GetDetectedMob / Npc_IsDetectedMobOwnedBy*: the mob the hero uses (or used last).
        /// </summary>
        private IInteractiveObject _heroDetectedMob;

        public void Update()
        {
            if (Time.time < _nextRoomCheckTime)
                return;
            _nextRoomCheckTime = Time.time + _roomCheckInterval;

            if (_gameStateService.GothicVm?.GlobalHero is not NpcInstance heroInstance)
                return;
            var hero = heroInstance.GetUserData();
            if (hero?.Go == null || hero.Props == null)
                return;

            var room = _roomService.GetRoomAt(GetFeetPosition(hero.Go));
            if (room.Equals(_roomService.HeroRoom, StringComparison.OrdinalIgnoreCase))
            {
                _pendingRoom = null;
                return;
            }

            if (!room.Equals(_pendingRoom, StringComparison.OrdinalIgnoreCase))
            {
                _pendingRoom = room;
                _pendingRoomChecks = 0;
            }
            if (++_pendingRoomChecks < _roomStableChecks)
                return;

            _pendingRoom = null;
            _roomService.SetHeroRoom(room);

            var notified = _npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessEnterRoom,
                hero.Instance, hero.Instance);
            Logger.Log($"[HeroPerc] AssessEnterRoom ('{_roomService.HeroFormerRoom}' -> '{_roomService.HeroRoom}') " +
                       $"sent to {notified} NPC(s)", LogCat.Ai);
        }

        /// <summary>
        /// VR: the rig's origin is on the floor. Flat: the player controller's centre is ~1 m above it.
        /// </summary>
        private static Vector3 GetFeetPosition(GameObject heroGo)
        {
            if (!heroGo.TryGetComponent<CharacterController>(out var controller))
                return heroGo.transform.position;

            return heroGo.transform.TransformPoint(controller.center + Vector3.down * (controller.height / 2f));
        }

        /// <summary>
        /// The hero grabs a mob (chest lid, door, bed, ...) or puts a lock pick in its lock.
        /// </summary>
        public void OnHeroUsesMob(IInteractiveObject mob)
        {
            if (mob == null)
                return;

            _heroDetectedMob = mob;
            if (mob == _lastUseMob && Time.time < _lastUseMobTime + _sameMobRepeatSeconds)
                return;
            _lastUseMob = mob;
            _lastUseMobTime = Time.time;

            if (_gameStateService.GothicVm?.GlobalHero is not NpcInstance heroInstance)
                return;
            var hero = heroInstance.GetUserData();

            var notified = _npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessUseMob,
                heroInstance, heroInstance);
            Logger.Log($"[HeroPerc] AssessUseMob '{mob.Name}' {mob.Visual?.Name} (owner '{(mob as IMovableObject)?.Owner}', " +
                       $"guild '{(mob as IMovableObject)?.OwnerGuild}') sent to {notified} NPC(s)", LogCat.Ai);
        }

        /// <summary>
        /// Npc_GetDetectedMob: the scheme name of the mob the hero uses, e.g. "CHESTBIG". Empty for nothing (only the
        /// hero's mob is known).
        /// </summary>
        public string GetHeroDetectedMobScheme()
        {
            return _heroDetectedMob.GetVisualScheme();
        }

        /// <summary>
        /// Npc_IsDetectedMobOwnedByNpc: the hero's mob belongs to the NPC (oCMob owner = the NPC's instance name).
        /// </summary>
        public bool IsHeroDetectedMobOwnedBy(NpcInstance npc)
        {
            var owner = (_heroDetectedMob as IMovableObject)?.Owner;
            if (npc == null || string.IsNullOrEmpty(owner))
                return false;
            var npcName = _gameStateService.GothicVm.GetSymbolByIndex(npc.Index)?.Name;
            return owner.Equals(npcName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Npc_IsDetectedMobOwnedByGuild: the hero's mob belongs to the guild (oCMob ownerGuild, e.g. "GIL_GRD").
        /// </summary>
        public bool IsHeroDetectedMobOwnedByGuild(int guild)
        {
            var ownerGuild = (_heroDetectedMob as IMovableObject)?.OwnerGuild;
            if (string.IsNullOrEmpty(ownerGuild))
                return false;
            try
            {
                var symbol = _gameStateService.GothicVm.GetSymbolByName(ownerGuild);
                var ownerGuildId = symbol != null ? symbol.GetInt(0) : int.TryParse(ownerGuild, out var id) ? id : -1;
                return ownerGuildId == guild;
            }
            catch (Exception e)
            {
                // A mod's owner guild that names no int constant.
                Logger.LogWarning($"[HeroPerc] Owner guild '{ownerGuild}' isn't a guild: {e.Message}", LogCat.Ai);
                return false;
            }
        }
    }
}
