using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Reflex.Attributes;
using ZenKit.Daedalus;
using static Gothic.Core.Models.Vm.VmGothicEnums;
using Object = UnityEngine.Object;

namespace Gothic.Core.Services.Npc
{
    public class NpcInventoryService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;

        
        public void ExtEquipItem(NpcInstance npc, int itemId)
        {
            var props = npc.GetUserData().Props;
            var itemData = _vmCacheService.TryGetItemData(itemId);
            
            props.EquippedItems.Add(itemData);
        }
        
        public void ExtCreateInvItems(NpcInstance npc, int itemIndex, int amount)
        {
            // FIXME - Does it make sense? It would mean we never add an item if we loaded a SaveGame...
            // We also initialize NPCs inside Daedalus when we load a save game. It's needed as some data isn't stored on save games.
            // But e.g., inventory items will be skipped as they are stored inside save game VOBs.
            // if (!_saveGameService.IsWorldEnteredFirstTime)
            //     return;

            if (npc.GetUserData() == null)
            {
                Logger.LogError($"NPC is not set for {nameof(ExtCreateInvItems)}. Is it an error on Daedalus or our end?", LogCat.Npc);
                return;
            }

            var itemInstance = _gameStateService.GothicVm.GetSymbolByIndex(itemIndex)!;
            var vob = npc.GetUserData()!.Vob;

            
            var mainFlag = (VmGothicEnums.ItemFlags)_vmCacheService.TryGetItemData(itemIndex).MainFlag;
            var inventoryCat = mainFlag.ToInventoryCategory();
            
            var items = vob.GetPacked((int)inventoryCat).UnpackItems();
            var itemFound = false;
            
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Name == itemInstance.Name)
                {
                    items[i].Amount += amount;
                    itemFound = true;
                    break;
                }
            }

            if (!itemFound)
                items.Add(new ContentItem(itemInstance.Name, amount));

            vob.SetPacked((int)inventoryCat, items.PackItems());
        }

        public void ExtRemoveInvItems(NpcInstance npc, int itemIndex, int amount)
        {
            if (npc.GetUserData() == null)
            {
                Logger.LogError($"NPC is not set for {nameof(ExtRemoveInvItems)}. Is it an error on Daedalus or our end?", LogCat.Npc);
                return;
            }

            var itemInstance = _gameStateService.GothicVm.GetSymbolByIndex(itemIndex)!;
            var vob = npc.GetUserData()!.Vob;
            
            var mainFlag = (VmGothicEnums.ItemFlags)_vmCacheService.TryGetItemData(itemIndex).MainFlag;
            var inventoryCat = mainFlag.ToInventoryCategory();
            
            var items = vob.GetPacked((int)inventoryCat).UnpackItems();
            var itemFound = false;
            
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Name == itemInstance.Name)
                {
                    var newAmount = items[i].Amount - amount;
                    
                    if (newAmount <= 0)
                        items.RemoveAt(i);
                    else
                        items[i].Amount -= amount;
    
                    itemFound = true;
                    break;
                }
            }

            if (!itemFound)
                return;

            vob.SetPacked((int)inventoryCat, items.PackItems());
        }

        public List<ContentItem> GetInventoryItems(NpcInstance npc, VmGothicEnums.InvCats category)
        {
            var npcVob = npc.GetUserData()!.Vob;
            return npcVob.GetPacked((int)category).UnpackItems();
        }

        /// <summary>
        /// Returns all items across every category. Each InvCats slot only exists in ZenKit if
        /// SetPacked was previously called for it — accessing a missing slot throws from native code.
        /// This method silently skips slots that were never initialized.
        /// </summary>
        public List<ContentItem> GetAllInventoryItems(NpcInstance npc)
        {
            var items = new List<ContentItem>();
            foreach (VmGothicEnums.InvCats cat in System.Enum.GetValues(typeof(VmGothicEnums.InvCats)))
            {
                if (cat == VmGothicEnums.InvCats.InvCatMax)
                    continue;
                try
                {
                    items.AddRange(GetInventoryItems(npc, cat));
                }
                catch
                {
                    // Slot was never initialized for this NPC — expected when a category has no items
                }
            }
            return items;
        }

        /// <summary>
        /// Npc_GetInvItem: the item if the NPC carries it - it also becomes the scripts' 'item' (G2: the Holy Hammer).
        /// </summary>
        public ItemInstance ExtNpcGetInvItem(NpcInstance npc, int itemId)
        {
            if (npc == null || itemId <= 0)
                return null;

            // ExtNpcHasItems needs the symbol's name.
            if (_gameStateService.GothicVm.GetSymbolByIndex(itemId) == null)
            {
                Logger.LogWarning($"Npc_GetInvItem: no symbol with index {itemId}.", LogCat.Npc);
                return null;
            }

            if (ExtNpcHasItems(npc, itemId) <= 0)
                return null;

            var item = _vmCacheService.TryGetItemData(itemId);
            if (item != null)
                _gameStateService.GothicVm.GlobalItem = item;
            return item;
        }

        public int ExtNpcHasItems(NpcInstance npc, int itemId)
        {
            var itemInstanceName = _gameStateService.GothicVm.GetSymbolByIndex(itemId)!.Name;

            foreach (InvCats cat in System.Enum.GetValues(typeof(InvCats)))
            {
                if (cat == InvCats.InvCatMax)
                    continue;
                try
                {
                    foreach (var item in GetInventoryItems(npc, cat))
                    {
                        if (string.Equals(item.Name, itemInstanceName, System.StringComparison.OrdinalIgnoreCase))
                            return item.Amount;
                    }
                }
                catch
                {
                    // Category slot was never initialized for this NPC
                }
            }

            return 0;
        }
        
        /// <summary>
        /// Npc_ClearInventory like the engine (OpenGothic Inventory::clear): everything goes except the items worn or
        /// held (equipped) and mission items. Found comparing with BoroBongo's branch.
        /// </summary>
        public void ExtNpcClearInventory(NpcInstance npc)
        {
            var container = npc?.GetUserData();
            if (container?.Vob == null)
                return;

            foreach (var item in GetAllInventoryItems(npc))
            {
                var itemData = _vmCacheService.TryGetItemData(item.Name);
                if (itemData == null || ((ItemFlags)itemData.Flags).HasFlag(ItemFlags.ItemMission) ||
                    container.Props.EquippedItems.Any(equipped => equipped?.Index == itemData.Index))
                    continue;

                ExtRemoveInvItems(npc, itemData.Index, item.Amount);
            }
        }

        /// <summary>
        /// The NPC takes off its melee and ranged weapons (into the inventory) - their meshes leave the belt and back
        /// too, a drawn one the hand (found on BoroBongo's branch).
        /// </summary>
        public void ExtAiUnequipWeapons(NpcInstance npc)
        {
            var container = npc?.GetUserData();
            if (container?.Props == null)
                return;

            container.Props.EquippedItems.RemoveAll(i =>
                i.MainFlag is (int)ItemFlags.ItemKatNf or (int)ItemFlags.ItemKatFf);

            // Like after AI_RemoveWeapon, a drawn weapon is gone and follow-up animations are weaponless. Fists stay.
            var weaponState = (WeaponState)container.Vob.FightMode;
            var isWeaponDrawn = weaponState is WeaponState.W1H or WeaponState.W2H or WeaponState.Bow or
                WeaponState.CBow;
            if (isWeaponDrawn)
                container.Vob.FightMode = (int)WeaponState.NoWeapon;
            if (container.Go == null)
                return;

            var slotNames = new List<string> { Constants.SlotSword, Constants.SlotLongsword, Constants.SlotBow,
                Constants.SlotCrossbow };
            if (isWeaponDrawn)
                slotNames.Add(DrawWeapon.GetHandSlotName(weaponState));
            foreach (var slotName in slotNames)
            {
                var slot = container.Go.FindChildRecursively(slotName);
                if (slot == null)
                    continue;

                for (var i = slot.transform.childCount - 1; i >= 0; i--)
                    Object.Destroy(slot.transform.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// Npc_GetInvItemBySlot: the amount of the slot-th item (1 based) of the inventory category - it also becomes
        /// the scripts' 'item'. Like the engine (OpenGothic Inventory::findByFlags): our packed inventory is stored per
        /// the same INV_* category. B_Plunder (G1) looks for ore in INV_MISC - before, NPCs who knocked out the hero
        /// said "not even ore" with 1000 in the backpack. Not found there: the equipped weapon/armor.
        /// </summary>
        public int ExtNpcGetInvItemBySlot(NpcInstance npc, int invType, int slot)
        {
            if (TryGetInvItemBySlot(npc, invType, slot, out var item, out var amount))
            {
                _gameStateService.GothicVm.GlobalItem = item;
                return amount;
            }

            var equipped = GetEquippedItemBySlot(npc?.GetUserData()?.Props?.EquippedItems, invType, slot);
            _gameStateService.GothicVm.GlobalItem = equipped;
            return equipped != null ? 1 : 0;
        }

        private bool TryGetInvItemBySlot(NpcInstance npc, int invType, int slot, out ItemInstance item, out int amount)
        {
            item = null;
            amount = 0;
            if (npc?.GetUserData() == null || slot < 1 || invType < (int)InvCats.InvWeapon ||
                invType >= (int)InvCats.InvCatMax)
                return false;

            List<ContentItem> items;
            try
            {
                items = GetInventoryItems(npc, (InvCats)invType);
            }
            catch
            {
                // Category slot was never initialized for this NPC - nothing in it.
                return false;
            }

            var found = 0;
            foreach (var contentItem in items)
            {
                if (contentItem.Amount <= 0)
                    continue;
                if (++found < slot)
                    continue;

                item = _vmCacheService.TryGetItemData(contentItem.Name);
                amount = contentItem.Amount;
                return item != null;
            }

            return false;
        }

        private static ItemInstance GetEquippedItemBySlot(List<ItemInstance> equippedItems, int invType,
            int slot)
        {
            if (equippedItems == null)
                return null;

            ItemFlags? mainFlag = (InvCats)invType switch
            {
                InvCats.InvWeapon when slot == 1 => ItemFlags.ItemKatNf,
                InvCats.InvWeapon when slot == 2 => ItemFlags.ItemKatFf,
                InvCats.InvArmor when slot == 1 => ItemFlags.ItemKatArmor,
                _ => null
            };
            return mainFlag == null ? null : equippedItems.FirstOrDefault(i => i.MainFlag == (int)mainFlag);
        }
    }
}
