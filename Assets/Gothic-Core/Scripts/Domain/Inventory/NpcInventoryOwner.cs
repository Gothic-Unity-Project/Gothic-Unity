using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Inventory;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Inventory
{
    /// <summary>
    /// An NPC's inventory (loot backpack, trader goods). Every change fires GlobalEventDispatcher.NpcInventoryChanged
    /// so the other views of the same NPC refresh.
    /// </summary>
    public class NpcInventoryOwner : IInventoryOwner
    {
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly GameStateService _gameStateService;

        private readonly string _hiddenItem;
        private readonly bool _isEquippedHidden;

        /// <summary>
        /// hiddenItem/isEquippedHidden: a trader's goods - without his currency and anything he wears or wields
        /// (the engine's T_Trade view).
        /// </summary>
        public NpcInventoryOwner(NpcContainer npc, string hiddenItem = null, bool isEquippedHidden = false)
        {
            Npc = npc;
            _hiddenItem = hiddenItem;
            _isEquippedHidden = isEquippedHidden;
            this.Inject();
        }

        public NpcContainer Npc { get; }

        public bool IsHero => false;

        public List<ContentItem> GetInventory(VmGothicEnums.InvCats category)
        {
            List<ContentItem> items;
            try
            {
                items = _npcInventoryService.GetInventoryItems(Npc.Instance, category);
            }
            catch (Exception)
            {
                // The category slot was never initialized for this NPC - nothing in it.
                items = new List<ContentItem>();
            }

            // Equipped weapons can be missing from the packed inventory (the loot panel lists them the same way).
            if (category == VmGothicEnums.InvCats.InvWeapon)
            {
                foreach (var equipped in Npc.Props.EquippedItems)
                {
                    var mainFlag = (VmGothicEnums.ItemFlags)equipped.MainFlag;
                    if (mainFlag != VmGothicEnums.ItemFlags.ItemKatNf && mainFlag != VmGothicEnums.ItemFlags.ItemKatFf)
                        continue;
                    var symbolName = _gameStateService.GothicVm.GetSymbolByIndex(equipped.Index)?.Name;
                    if (symbolName != null && !items.Any(i => i.Name.EqualsIgnoreCase(symbolName)))
                        items.Add(new ContentItem(symbolName, 1));
                }
            }

            return items.Where(i => CanTake(i.Name)).ToList();
        }

        public void Add(string itemInstanceName, int amount)
        {
            var item = _vmCacheService.TryGetItemData(itemInstanceName);
            if (item == null)
            {
                Logger.LogWarning($"[NpcInventoryOwner] Unknown item {itemInstanceName} - not added.", LogCat.Npc);
                return;
            }

            _npcInventoryService.ExtCreateInvItems(Npc.Instance, item.Index, amount);
            GlobalEventDispatcher.NpcInventoryChanged.Invoke(Npc);
        }

        public void Remove(string itemInstanceName, int amount)
        {
            var item = _vmCacheService.TryGetItemData(itemInstanceName);
            if (item == null)
            {
                Logger.LogWarning($"[NpcInventoryOwner] Unknown item {itemInstanceName} - not removed.", LogCat.Npc);
                return;
            }

            if (GetOwnedAmount(item, itemInstanceName) <= amount && IsEquipped(itemInstanceName))
                TakeOff(item, itemInstanceName);

            _npcInventoryService.ExtRemoveInvItems(Npc.Instance, item.Index, amount);
            GlobalEventDispatcher.NpcInventoryChanged.Invoke(Npc);
        }

        public bool IsEquipped(string itemInstanceName)
        {
            return _npcInventoryService.IsEquipped(Npc.Instance, itemInstanceName);
        }

        /// <summary>
        /// Worn armor stays on the NPC (its visual is the body).
        /// </summary>
        public bool CanTake(string itemInstanceName)
        {
            var item = _vmCacheService.TryGetItemData(itemInstanceName);
            if (item == null)
                return false;
            if (_hiddenItem != null && itemInstanceName.EqualsIgnoreCase(_hiddenItem))
                return false;
            if (_isEquippedHidden && IsEquipped(itemInstanceName))
                return false;
            var isArmor = ((VmGothicEnums.ItemFlags)item.MainFlag).ToInventoryCategory() ==
                          VmGothicEnums.InvCats.InvArmor;
            return !(isArmor && IsEquipped(itemInstanceName));
        }

        private int GetOwnedAmount(ItemInstance item, string itemInstanceName)
        {
            var category = ((VmGothicEnums.ItemFlags)item.MainFlag).ToInventoryCategory();
            return GetInventory(category).Where(i => i.Name.EqualsIgnoreCase(itemInstanceName)).Sum(i => i.Amount);
        }

        private void TakeOff(ItemInstance item, string itemInstanceName)
        {
            var mainFlag = (VmGothicEnums.ItemFlags)item.MainFlag;
            if (mainFlag is VmGothicEnums.ItemFlags.ItemKatNf or VmGothicEnums.ItemFlags.ItemKatFf)
                _npcInventoryService.RemoveEquippedWeapon(Npc, itemInstanceName);
            else
                _npcInventoryService.UnequipItemWithEffects(Npc.Instance, item);
        }
    }
}
