using System;
using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Logging;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Player
{
    public class PlayerService
    {
        public Vector3 HeroSpawnPosition;
        public Quaternion HeroSpawnRotation;
        
        public string LastLevelChangeTriggerVobName;
        public NpcContainer HeroContainer { get; private set; }
        
        
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        [Inject] private readonly SaveGameService _saveGameService;


        // Air in lungs isn't stored in G1 savegame, therefore setting it here temporarily at runtime.
        [NonSerialized] public bool IsDiving;
        [NonSerialized] public float MaxAir;
        [NonSerialized] public bool HasMaxAir; // Faster check for other components. Rather than calculating value on their own each frame.
        [NonSerialized] public float CurrentAir = float.MaxValue;
        
        
        public PlayerService()
        {
            GlobalEventDispatcher.LockPickComboBroken.AddListener(
                (lockPick, _, _) => RemoveItem(lockPick.VobAs<IItem>().Instance, 1));
            
            GlobalEventDispatcher.ZenKitBootstrapped.AddListener(OnZenKitBootstrapped);
        }

        private void OnZenKitBootstrapped()
        {
            MaxAir = _gameStateService.GuildValues.GetDiveTime((int)VmGothicEnums.Guild.GIL_HUMAN);
            CurrentAir = MaxAir; // Isn't stored in SaveGame, therefore setting it to full now.

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

            _saveGameService.CurrentWorldData.Vobs.Remove(item.Vob);
            AddItem(item);
        }

        /// <summary>
        /// Inverse of PickUpItem(): The item leaves the inventory and is part of the world's VOB list again.
        /// keepOutOfWorld: Another holder (hand, holster) already has the item. Then only the inventory is updated.
        /// </summary>
        public void DropItem(VobContainer item, bool addCulling = false, bool keepOutOfWorld = false)
        {
            if (!keepOutOfWorld)
            {
                var worldVobs = _saveGameService.CurrentWorldData.Vobs;
                if (!worldVobs.Contains(item.Vob))
                    worldVobs.Add(item.Vob);
            }

            if (addCulling)
                _vobMeshCullingService.AddCullingEntry(item);

            RemoveItem(item);
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
            symbolName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
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

        private IEnumerator HandleStatusEffects()
        {
            while (true)
            {
                if (IsDiving)
                {
                    HasMaxAir = false;
                    
                    if (CurrentAir > 0)
                        CurrentAir -= Time.deltaTime;
                }
                else if (!HasMaxAir)
                {
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
