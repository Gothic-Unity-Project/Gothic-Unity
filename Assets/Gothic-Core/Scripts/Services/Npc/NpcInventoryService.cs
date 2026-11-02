using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using ZenKit.Daedalus;
using static Gothic.Core.Models.Vm.VmGothicEnums;

namespace Gothic.Core.Services.Npc
{
    public class NpcInventoryService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VobService _vobService;

        [Inject] private readonly EquipmentService _equipmentService;

        
        public void ExtEquipItem(NpcInstance npc, int itemId)
        {
            _equipmentService.EquipByScript(npc.GetUserData(), _vmCacheService.TryGetItemData(itemId));
        }

        /// <summary>
        /// AI_EquipArmor like the engine (OpenGothic Inventory::equipArmor): an armor the NPC carries is put on. Its
        /// look shows the next time the NPC is built - the body mesh isn't rebuilt while the NPC stands in the world.
        /// </summary>
        public void ExtAiEquipArmor(NpcInstance npc, int itemId)
        {
            var item = itemId > 0 ? _vmCacheService.TryGetItemData(itemId) : null;
            if (item == null || !((ItemFlags)item.MainFlag).HasFlag(ItemFlags.ItemKatArmor))
            {
                Logger.LogWarning($"[Equipment] AI_EquipArmor: {npc.GetName(NpcNameSlot.Slot0)} - item {itemId} is " +
                    (item == null ? "no item" : $"{item.Name}, no armor"), LogCat.Npc);
                return;
            }

            if (ExtNpcHasItems(npc, itemId) <= 0)
                return;

            WearArmor(npc, item);
        }

        /// <summary>
        /// AI_EquipBestArmor like the engine (OpenGothic Inventory::bestItem): the carried armor the NPC may wear with
        /// the most damage, then the highest value.
        /// </summary>
        public void ExtAiEquipBestArmor(NpcInstance npc)
        {
            var container = npc.GetUserData();
            if (container == null)
                return;

            ItemInstance best = null;
            foreach (var content in GetAllInventoryItems(npc))
            {
                var item = _vmCacheService.TryGetItemData(content.Name);
                if (item == null || !((ItemFlags)item.MainFlag).HasFlag(ItemFlags.ItemKatArmor) ||
                    !_equipmentService.MeetsConditions(container, item))
                    continue;
                if (best == null || (item.DamageTotal, item.Value).CompareTo((best.DamageTotal, best.Value)) > 0)
                    best = item;
            }

            if (best != null)
                WearArmor(npc, best);
        }

        /// <summary>
        /// AI_UnequipArmor: the worn armor comes off - its protection too, and the NPC is built with its plain body
        /// the next time.
        /// </summary>
        public void ExtAiUnequipArmor(NpcInstance npc)
        {
            var container = npc.GetUserData();
            if (container?.Props == null)
                return;

            foreach (var worn in container.Props.EquippedItems.ToArray())
            {
                // The torso armor only - a helmet stays on.
                if (worn != null && ((ItemFlags)worn.MainFlag).HasFlag(ItemFlags.ItemKatArmor) &&
                    !EquipmentService.IsHelmet(worn))
                    _equipmentService.Unequip(container, worn);
            }

            container.Props.BodyData.Armor = -1;
            container.Props.MdmName = container.Props.BodyData.Body;
        }

        private void WearArmor(NpcInstance npc, ItemInstance item)
        {
            var container = npc.GetUserData();
            if (container?.Props == null)
                return;

            _equipmentService.EquipByScript(container, item);
            Logger.Log($"[Equipment] {npc.GetName(NpcNameSlot.Slot0)} puts on {item.Name}.", LogCat.Npc);

            // A helmet (DoM's slave shackle) doesn't change the body. It isn't drawn yet.
            if (EquipmentService.IsHelmet(item))
                return;

            // Without a visual_change the NPC would get no body mesh at all - it keeps the one it has.
            if (string.IsNullOrEmpty(item.VisualChange))
            {
                Logger.LogWarning($"[Equipment] {npc.GetName(NpcNameSlot.Slot0)}: armor {item.Name} has no " +
                                  "visual_change - the body stays as it is.", LogCat.Npc);
                return;
            }

            container.Props.BodyData.Armor = item.Index;
            container.Props.MdmName = item.VisualChange;
        }
        
        public void ExtCreateInvItems(NpcInstance npc, int itemIndex, int amount)
        {
            // FIXME - Does it make sense? It would mean we never add an item if we loaded a SaveGame...
            // We also initialize NPCs inside Daedalus when we load a save game. It's needed as some data isn't stored on save games.
            // But e.g., inventory items will be skipped as they are stored inside save game VOBs.
            // if (!_saveGameService.IsWorldLoadedForTheFirstTime)
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
            
            var items = _vobService.UnpackItems(vob.GetPacked((int)inventoryCat));
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

            vob.SetPacked((int)inventoryCat, _vobService.PackItems(items));
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
            
            var items = _vobService.UnpackItems(vob.GetPacked((int)inventoryCat));
            var itemFound = false;
            
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Name == itemInstance.Name)
                {
                    var newAmount = items[i].Amount - amount;
                    
                    if (newAmount <= 0)
                    {
                        items.RemoveAt(i);
                        var container = npc.GetUserData();
                        _equipmentService.OnItemGone(container, _vmCacheService.TryGetItemData(itemIndex),
                            () => ExtNpcHasItems(npc, itemIndex) <= 0);
                    }
                    else
                        items[i].Amount -= amount;
    
                    itemFound = true;
                    break;
                }
            }

            if (!itemFound)
                return;

            vob.SetPacked((int)inventoryCat, _vobService.PackItems(items));
        }

        public List<ContentItem> GetInventoryItems(NpcInstance npc, VmGothicEnums.InvCats category)
        {
            var npcVob = npc.GetUserData()!.Vob;
            return _vobService.UnpackItems(npcVob.GetPacked((int)category));
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
        
        public void ExtNpcClearInventory(NpcInstance npc)
        {
            npc.GetUserData()!.Vob.ClearItems();
        }
    }
}
