using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Vm;
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
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly VmService _vmService;

        /// <summary>
        /// Like the engine (OpenGothic Inventory::use): the item's use function (on_state[0]) runs with the NPC as self
        /// and the item as item - a potion heals, food feeds, a letter or map is shown (Doc_*). The caller takes care of
        /// the item itself (eaten or kept).
        /// </summary>
        public void UseItem(NpcContainer npc, ItemInstance item)
        {
            var function = item.GetOnState(ItemStateSlot.Slot0);
            if (function <= 0 || npc?.Instance == null)
            {
                Logger.LogWarning($"[NpcInventory] {item.Name} not used: " +
                    (npc?.Instance == null ? "no NPC" : "it has no use function"), LogCat.Npc);
                return;
            }

            var vm = _gameStateService.GothicVm;
            var oldItem = vm.GlobalItem;
            vm.GlobalItem = item;
            try
            {
                Logger.Log($"[NpcInventory] {npc.Instance.GetName(NpcNameSlot.Slot0)} uses {item.Name}", LogCat.Npc);
                _vmService.CallAsNpc(function, npc.Instance, null, null, LogCat.Npc);
            }
            finally
            {
                vm.GlobalItem = oldItem;
            }
        }

        [Inject] private readonly EquipmentService _equipmentService;

        /// <summary>
        /// Like the engine, an item is used up by its use if the end of its use animation (t_[scheme]_S0_2_Stand)
        /// destroys it: food, potions, G1's sealed letter (MAPSEALED). A map (MAP) or a book stays. Found on BoroBongo's
        /// branch.
        /// </summary>
        public bool IsUsedUpOnUse(ItemInstance item)
        {
            if (string.IsNullOrEmpty(item?.SchemeName))
                return false;

            var animationName = $"t_{item.SchemeName}_S0_2_Stand";
            var humans = _resourceCacheService.TryGetModelScript("Humans");
            if (humans == null)
                return false;

            foreach (var animation in humans.Animations)
            {
                if (!animation.Name.EqualsIgnoreCase(animationName))
                    continue;

                foreach (var tag in animation.EventTags)
                {
                    if (tag.Type == ZenKit.EventType.ItemDestroy)
                        return true;
                }
            }

            return false;
        }

        
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
                if (worn != null && ((ItemFlags)worn.MainFlag).HasFlag(ItemFlags.ItemKatArmor))
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
            container.Props.BodyData.Armor = item.Index;
            container.Props.MdmName = item.VisualChange;
            Logger.Log($"[Equipment] {npc.GetName(NpcNameSlot.Slot0)} puts on {item.Name}.", LogCat.Npc);
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
    }
}
