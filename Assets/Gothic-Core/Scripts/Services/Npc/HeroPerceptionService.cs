using Gothic.Core.Models.Container;
using System;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
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
    /// - PERC_ASSESSENTERROOM when he walks into another portal room (RoomService),
    /// - PERC_ASSESSTHEFT when he takes an item that lay in the world.
    /// The Daedalus scripts decide what the NPCs do with it (owner's chest, whose hut, ...).
    /// </summary>
    public class HeroPerceptionService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly RoomService _roomService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly VmCacheService _vmCacheService;

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
        /// Snd_IsSourceNpc / Npc_CanSeeSource: who made the last noise (the hero's steps come with the movement PR).
        /// </summary>
        public NpcContainer SoundSourceNpc { get; private set; }

        // The items lying in the world since it was loaded. Taking one of them is what NPCs can see as theft - items
        // the hero carried before (e.g. taken out of his backpack) are his. ZenKit hands out a new object for the same
        // VOB each time a parent's children are read, so the loaded items are known by their index in the world file
        // (Id) and only the ones created since (Wld_InsertItem, Id 0) by their object.
        private readonly HashSet<int> _worldItemIds = new();
        private readonly HashSet<IVirtualObject> _insertedWorldItems = new();

        public HeroPerceptionService()
        {
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(OnWorldLoaded);
            GlobalEventDispatcher.WorldItemInserted.AddListener(AddWorldItem);
        }

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

            // Like the engine (OpenGothic MoveAlgo), sneaking into a room isn't noticed - the room changes anyway.
            if (hero.Props.BodyState == VmGothicEnums.BodyState.BsSneak)
                return;

            if (_npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessEnterRoom,
                    hero.Instance, hero.Instance))
                Logger.Log($"[HeroPerc] AssessEnterRoom ('{_roomService.HeroFormerRoom}' -> " +
                           $"'{_roomService.HeroRoom}') sent", LogCat.Ai);
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

            if (_npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessUseMob,
                    heroInstance, heroInstance))
                Logger.Log($"[HeroPerc] AssessUseMob '{mob.Name}' {mob.Visual?.Name} " +
                           $"(owner '{(mob as IMovableObject)?.Owner}', guild '{(mob as IMovableObject)?.OwnerGuild}') " +
                           "sent", LogCat.Ai);
        }

        /// <summary>
        /// The hero took an item into his inventory. Only the first take of an item that lay in the world is noticed:
        /// PERC_ASSESSTHEFT with the item as the scripts' 'item' and no victim (B_AssessTheft reads a victim as
        /// picked pockets).
        /// </summary>
        public void OnHeroTookItem(IVirtualObject itemVob)
        {
            if (itemVob is not IItem itemVobData || !RemoveWorldItem(itemVob))
                return;

            var instanceName = !string.IsNullOrEmpty(itemVobData.Instance) ? itemVobData.Instance : itemVob.Name;
            var item = _vmCacheService.TryGetItemData(instanceName);
            var hero = (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();
            if (item == null || hero == null)
            {
                Logger.LogWarning($"[HeroPerc] The hero takes {instanceName} - no theft perception: " +
                                  (item == null ? "no item data" : "no hero"), LogCat.Ai);
                return;
            }

            if (_npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessTheft, null,
                    hero.Instance, item))
                Logger.Log($"[HeroPerc] AssessTheft '{item.Name}' sent", LogCat.Ai);
        }

        private void OnWorldLoaded()
        {
            _worldItemIds.Clear();
            _insertedWorldItems.Clear();
            HeroDetectedMob = null;
            _lastUseMob = null;
            if (_saveGameService.CurrentWorldName == null)
                return;

            AddWorldItems(_saveGameService.CurrentWorldData.Vobs);
            Logger.Log($"[HeroPerc] {_worldItemIds.Count + _insertedWorldItems.Count} items lie in the world",
                LogCat.Ai);
        }

        private bool RemoveWorldItem(IVirtualObject itemVob)
        {
            return itemVob.Id != 0 ? _worldItemIds.Remove(itemVob.Id) : _insertedWorldItems.Remove(itemVob);
        }

        private void AddWorldItems(IEnumerable<IVirtualObject> vobs)
        {
            if (vobs == null)
                return;

            foreach (var vob in vobs)
            {
                AddWorldItem(vob);
                AddWorldItems(vob.Children);
            }
        }

        private void AddWorldItem(IVirtualObject vob)
        {
            if (vob is not IItem)
                return;

            if (vob.Id != 0)
                _worldItemIds.Add(vob.Id);
            else
                _insertedWorldItems.Add(vob);
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
