using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Logging;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Player
{
    public class PlayerService
    {
        public Vector3 HeroSpawnPosition;
        public Quaternion HeroSpawnRotation;
        
        public string LastLevelChangeTriggerVobName;


        /// <summary>
        /// God mode (Marvin): hits and falls don't hurt the hero.
        /// </summary>
        public bool IsGodMode;

        /// <summary>
        /// Marvin's focus like Gothic's: the NPC last pointed at (VR: hovered with a hand - VRFocus), kept until
        /// another one is or the world changes. Only set with DeveloperConfig.EnableMarvinKill, for its "kill".
        /// </summary>
        public NpcContainer FocusNpc;
        public NpcContainer HeroContainer { get; private set; }
        
        
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly Npc.HeroPerceptionService _heroPerceptionService;


        // Air in lungs isn't stored in G1 savegame, therefore setting it here temporarily at runtime.
        [NonSerialized] public bool IsDiving;
        [NonSerialized] public float MaxAir;
        [NonSerialized] public bool HasMaxAir; // Faster check for other components. Rather than calculating value on their own each frame.
        [NonSerialized] public float CurrentAir = float.MaxValue;

        // Out of air: like the engine (OpenGothic Npc::tick), 1 hit point per NPC_DAM_DIVE_TIME ms (G1: 100).
        private const float _defaultDrownSecondsPerHitPoint = 0.1f;
        private float _drownSecondsPerHitPoint = _defaultDrownSecondsPerHitPoint;
        private float _drownTime;

        // NpcService injects PlayerService (hero spawn, SetHero, AddItem) - injected here, Reflex would build both
        // forever. Resolved on first use.
        private NpcService _npcService;
        private NpcService NpcService => _npcService ??= ReflexProjectInstaller.DIContainer.Resolve<NpcService>();
        
        
        public PlayerService()
        {
            GlobalEventDispatcher.LockPickComboBroken.AddListener(
                (lockPick, _, _) => RemoveItem(lockPick.VobAs<IItem>().Instance, 1));
            
            GlobalEventDispatcher.ZenKitBootstrapped.AddListener(OnZenKitBootstrapped);
            GlobalEventDispatcher.LoadGameStart.AddListener(ResetDrowning);
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(() => FocusNpc = null);
        }

        private void OnZenKitBootstrapped()
        {
            MaxAir = _gameStateService.GuildValues.GetDiveTime((int)VmGothicEnums.Guild.GIL_HUMAN);
            CurrentAir = MaxAir; // Isn't stored in SaveGame, therefore setting it to full now.
            var drownMilliseconds = _gameStateService.GothicVm.GetSymbolByName("NPC_DAM_DIVE_TIME")?.GetInt(0) ?? 0;
            _drownSecondsPerHitPoint = drownMilliseconds > 0
                ? drownMilliseconds / 1000f
                : _defaultDrownSecondsPerHitPoint;

            _unityMonoService.StartCoroutine(HandleStatusEffects());
        }
        
        public void SetHero(NpcContainer heroContainer)
        {
            HeroContainer = heroContainer;
        }
        
        public void ResetSpawn()
        {
            HeroSpawnPosition = default;
            HeroSpawnRotation = default;
        }

        public List<ContentItem> GetInventory(VmGothicEnums.InvCats category)
        {
            return _npcInventoryService.GetInventoryItems(HeroContainer.Instance, category);
        }

        public void AddItem(string itemInstanceName, int amount = 1)
        {
            var item = _vmCacheService.TryGetItemData(itemInstanceName)!;
            _npcInventoryService.ExtCreateInvItems(HeroContainer.Instance, item.Index, amount);
        }

        public void RemoveItem(string itemInstanceName, int amount = 1)
        {
            var item = _vmCacheService.TryGetItemData(itemInstanceName)!;
            _npcInventoryService.ExtRemoveInvItems(HeroContainer.Instance, item.Index, amount);
        }

        /// <summary>
        /// Rule: A world item in a hand, holster, backpack slot or shoulder stash is in the hero's inventory and
        /// therefore not part of the world's VOB list any longer (otherwise it would be saved twice).
        /// Hands and holsters keep the culling entry (the item stays loaded and is only paused while moved).
        /// Backpack slots and the stash let the item leave the culling.
        /// </summary>
        public void PickUpItem(VobContainer item, bool removeCulling = false)
        {
            if (removeCulling)
                _vobMeshCullingService.RemoveCullingEntry(item);

            _saveGameService.RemoveVobFromWorld(item.Vob);
            AddItem(item);
            _heroPerceptionService.OnHeroTookItem(item.Vob);
        }

        /// <summary>
        /// Inverse of PickUpItem(): The item leaves the inventory and is part of the world's VOB list again.
        /// keepOutOfWorld: Another holder (hand, holster) already has the item. Then only the inventory is updated.
        /// </summary>
        public void DropItem(VobContainer item, bool addCulling = false, bool keepOutOfWorld = false)
        {
            if (!keepOutOfWorld)
                _saveGameService.AddVobToWorld(item.Vob);

            if (addCulling)
                _vobMeshCullingService.AddCullingEntry(item);

            RemoveItem(item);
            UnequipIfNoneLeft(item);
        }

        /// <summary>
        /// Like Gothic, an equipped item comes off when the last one leaves the inventory (dropped into the world).
        /// </summary>
        private void UnequipIfNoneLeft(VobContainer item)
        {
            var instance = item.GetItemInstance();
            var symbolName = (item.Vob as IItem)?.GetSymbolName();
            if (instance == null || symbolName == null || HeroContainer == null ||
                !_npcInventoryService.IsEquipped(HeroContainer.Instance, symbolName))
                return;
            var category = ((VmGothicEnums.ItemFlags)instance.MainFlag).ToInventoryCategory();
            if (GetInventory(category).Any(i => i.Name.EqualsIgnoreCase(symbolName) && i.Amount > 0))
                return;
            _npcInventoryService.UnequipItemWithEffects(HeroContainer.Instance, instance);
            Logger.Log($"[PlayerService] {instance.Name} dropped - no longer equipped", LogCat.Vob);
        }

        /// <summary>
        /// Only the inventory count (no world handling). Items are stored by their symbol name (Instance).
        /// </summary>
        public void AddItem(VobContainer item)
        {
            if (TryGetItemSymbol(item, out var symbolName, out var amount))
                AddItem(symbolName, amount);
        }

        public void RemoveItem(VobContainer item)
        {
            if (TryGetItemSymbol(item, out var symbolName, out var amount))
                RemoveItem(symbolName, amount);
        }

        private static bool TryGetItemSymbol(VobContainer item, out string symbolName, out int amount)
        {
            symbolName = null;
            amount = 0;

            if (item.Vob is not IItem vobItem)
            {
                Logger.LogWarning($"Vob >{item.Vob?.Name}< isn't an item. Inventory is not changed.", LogCat.Vob);
                return false;
            }

            // ItemInstance.Name is a display name. Only the Instance (symbol) is known to the VM.
            symbolName = vobItem.GetSymbolName();
            amount = Mathf.Max(1, vobItem.Amount);
            return true;
        }

        public void StartDiving()
        {
            IsDiving = true;
        }

        public void StopDiving()
        {
            IsDiving = false;
        }

        /// <summary>
        /// The hero dove longer than his DIVE_TIME: he loses hit points until he surfaces or dies.
        /// </summary>
        private void Drown()
        {
            if (HeroContainer?.Vob == null)
                return;

            var hitPoints = HeroContainer.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            if (hitPoints <= 0)
            {
                // Dead (drowned or killed under water): a hero brought back starts the count over.
                ResetDrowning();
                return;
            }

            var before = (int)(_drownTime / _drownSecondsPerHitPoint);
            _drownTime += Time.deltaTime;
            var damage = (int)(_drownTime / _drownSecondsPerHitPoint) - before;
            if (damage <= 0)
                return;

            // Drowned at 0: the hero dies like after a script taking his last hit points (FightService decides).
            NpcService.ReduceHitPoints(HeroContainer, damage);
        }

        /// <summary>
        /// A new or loaded game, or a dead hero, starts the drowning count over.
        /// </summary>
        private void ResetDrowning()
        {
            _drownTime = 0f;
        }

        private IEnumerator HandleStatusEffects()
        {
            while (true)
            {
                if (IsDiving)
                {
                    HasMaxAir = false;
                    
                    if (CurrentAir > 0)
                        CurrentAir -= Time.deltaTime;
                    else
                        Drown();
                }
                else if (!HasMaxAir)
                {
                    _drownTime = 0f;
                    if (CurrentAir < MaxAir)
                        CurrentAir += Time.deltaTime;
                    else
                    {
                        CurrentAir = MaxAir;
                        HasMaxAir = true;
                    }
                }
                
                yield return null;
            }
            // ReSharper disable once IteratorNeverReturns
        }
    }
}
