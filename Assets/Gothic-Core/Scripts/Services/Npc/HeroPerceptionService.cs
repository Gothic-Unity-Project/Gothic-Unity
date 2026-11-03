using Gothic.Core.Models.Container;
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
    /// - PERC_ASSESSQUIETSOUND for his footsteps - not while sneaking,
    /// - PERC_ASSESSTHEFT when he takes an item lying in the world,
    /// - PERC_ASSESSENTERROOM when he walks into another portal room (RoomService).
    /// The Daedalus scripts decide what the NPCs do with it (owner's chest, whose hut, ...).
    /// </summary>
    public class HeroPerceptionService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly RoomService _roomService;
        [Inject] private readonly Gothic.Core.Services.Caches.VmCacheService _vmCacheService;
        [Inject] private readonly Gothic.Core.Services.Caches.MultiTypeCacheService _multiTypeCacheService;

        private const float _walkStepInterval = 0.55f;
        private const float _runStepInterval = 0.35f;
        private float _nextStepTime;

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
        public IInteractiveObject HeroDetectedMob { get; private set; }

        /// <summary>
        /// Snd_IsSourceNpc / Npc_CanSeeSource: who made the last noise.
        /// </summary>
        public NpcContainer SoundSourceNpc { get; private set; }

        public void Update()
        {
            UpdateFootsteps();
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
        /// The bottom of the hero's body collider: VR the rig's CharacterController (under the HMD, also roomscale),
        /// Flat the player's capsule (its centre is ~1 m above the floor). No collider: the object's origin.
        /// </summary>
        private static Vector3 GetFeetPosition(GameObject heroGo)
        {
            if (!heroGo.TryGetComponent<Collider>(out var body))
                return heroGo.transform.position;

            var bounds = body.bounds;
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        /// <summary>
        /// The hero grabs a mob (chest lid, door, bed, ...) or puts a lock pick in its lock.
        /// </summary>
        public void OnHeroUsesMob(IInteractiveObject mob)
        {
            if (mob == null)
                return;

            HeroDetectedMob = mob;
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
        /// Like the engine taking an item (OpenGothic Npc::takeItem), the hero
        /// taking an item lying in the world sends PERC_ASSESSTHEFT with it as the global item. The scripts decide
        /// (B_AssessTheft: the item's owner, the owner of the room - "Thief!" in Lares' hut or a citizen's house).
        /// Only the first take of a world item counts, nothing out of an inventory (VobContainer.IsTakenWithoutTheft).
        /// </summary>
        public void OnHeroTakesItem(VobContainer container)
        {
            if (container == null || container.IsTakenWithoutTheft ||
                container.Vob is not IItem vobItem)
                return;
            container.IsTakenWithoutTheft = true;

            if (_gameStateService.GothicVm?.GlobalHero is not NpcInstance heroInstance)
                return;
            var hero = heroInstance.GetUserData();
            var instanceName = vobItem.GetSymbolName();
            var item = _vmCacheService.TryGetItemData(instanceName);
            if (hero == null || item == null)
                return;

            var vm = _gameStateService.GothicVm;
            var oldItem = vm.GlobalItem;
            var oldVictim = vm.GlobalVictim;
            // Like the engine, a taken item has no victim - the scripts tell it from pickpocketing by "item && !victim"
            // (G1's B_AssessTheft): with the hero as the victim, Ghorri warned him "Behind you!" for every item picked
            // up on the jetty.
            try
            {
                vm.GlobalItem = item;
                vm.GlobalVictim = null;
                LogTheftChecks(hero, heroInstance, item);
                var notified = _npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessTheft,
                    null, heroInstance);
                Logger.Log($"[HeroPerc] AssessTheft '{instanceName}' (room '{_roomService.HeroRoom}') sent to " +
                           $"{notified} NPC(s)", LogCat.Ai);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[HeroPerc] AssessTheft '{instanceName}' failed: {e.Message}", LogCat.Ai);
            }
            finally
            {
                vm.GlobalItem = oldItem;
                vm.GlobalVictim = oldVictim;
            }
        }

        // Diagnostics: who could call the theft and why not.
        private const float _theftDiagnosticRange = 10f;

        /// <summary>
        /// What G2's B_AssessTheft checks, per NPC nearby: height, sight, the room's guild and its helpers
        /// (C_IsTakenItemMyPossession, C_WantToAttackThief) - a theft nobody reacted to shows the failing check.
        /// </summary>
        private void LogTheftChecks(NpcContainer hero, NpcInstance heroInstance, ItemInstance item)
        {
            var vm = _gameStateService.GothicVm;
            var possession = vm.GetSymbolByName("C_IsTakenItemMyPossession");
            var wantAttack = vm.GetSymbolByName("C_WantToAttackThief");
            var heroPosition = hero.Go.transform.position;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc?.Go == null || npc == hero || npc.Instance == null ||
                    (npc.Go.transform.position - heroPosition).sqrMagnitude > _theftDiagnosticRange * _theftDiagnosticRange)
                    continue;

                var oldSelf = vm.GlobalSelf;
                var oldOther = vm.GlobalOther;
                try
                {
                    vm.GlobalSelf = npc.Instance;
                    vm.GlobalOther = heroInstance;
                    var hasPerception = npc.Props.Perceptions.TryGetValue(VmGothicEnums.PerceptionType.AssessTheft,
                        out var function) && function >= 0;
                    var isMine = possession != null &&
                                 vm.Call<int, NpcInstance, NpcInstance, ItemInstance>(possession.Index, npc.Instance,
                                     heroInstance, item) != 0;
                    var wants = wantAttack != null &&
                                vm.Call<int, NpcInstance, NpcInstance>(wantAttack.Index, npc.Instance, heroInstance) != 0;
                    var height = _npcAiService.ExtNpcGetHeightToNpc(npc.Instance, heroInstance);
                    var canSee = _npcAiService.ExtNpcCanSeeNpc(npc.Instance, heroInstance, false);
                    Logger.Log($"[HeroPerc] Theft check {npc.Instance.GetName(NpcNameSlot.Slot0)} (guild " +
                               $"{npc.Instance.Guild}): perception={hasPerception} height={height} canSee={canSee} " +
                               $"roomGuild={_roomService.GuildOfRoom(_roomService.HeroRoom)} myPossession={isMine} " +
                               $"wantsToAttack={wants}", LogCat.Ai);
                }
                catch (Exception e)
                {
                    Logger.LogWarning($"[HeroPerc] Theft check failed: {e.Message}", LogCat.Ai);
                }
                finally
                {
                    vm.GlobalSelf = oldSelf;
                    vm.GlobalOther = oldOther;
                }
            }
        }

        /// <summary>
        /// Like OpenGothic: every footstep is a quiet sound unless the hero sneaks (BS_SNEAK). G1's B_AssessQuietSound
        /// only reacts to noises with an item (Npc_GetDistToItem), so there footsteps stay without effect - as in the engine.
        /// </summary>
        private void UpdateFootsteps()
        {
            if (_gameStateService.GothicVm?.GlobalHero is not NpcInstance heroInstance)
                return;
            var hero = heroInstance.GetUserData();
            if (hero?.Props == null)
                return;

            var bodyState = hero.Props.BodyState;
            if (bodyState is not (VmGothicEnums.BodyState.BsWalk or VmGothicEnums.BodyState.BsRun))
                return;
            if (Time.time < _nextStepTime)
                return;
            _nextStepTime = Time.time + (bodyState == VmGothicEnums.BodyState.BsRun ? _runStepInterval : _walkStepInterval);

            SoundSourceNpc = hero;
            var vm = _gameStateService.GothicVm;
            var oldItem = vm.GlobalItem;
            try
            {
                // No item makes this noise - a stale global item would make NPCs walk to it.
                vm.GlobalItem = null;
                _npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessQuietSound,
                    hero.Instance, hero.Instance);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[HeroPerc] Footstep quiet sound failed: {e.Message}", LogCat.Ai);
            }
            finally
            {
                vm.GlobalItem = oldItem;
            }
        }

        /// <summary>
        /// Npc_GetDetectedMob: the scheme name of the mob the hero uses, e.g. "CHESTBIG". Empty for nothing (only the
        /// hero's mob is known).
        /// </summary>
        public string GetHeroDetectedMobScheme()
        {
            return HeroDetectedMob.GetVisualScheme();
        }

        /// <summary>
        /// Npc_IsDetectedMobOwnedByNpc: the hero's mob belongs to the NPC (oCMob owner = the NPC's instance name).
        /// </summary>
        public bool IsHeroDetectedMobOwnedBy(NpcInstance npc)
        {
            var owner = (HeroDetectedMob as IMovableObject)?.Owner;
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
            var ownerGuild = (HeroDetectedMob as IMovableObject)?.OwnerGuild;
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
