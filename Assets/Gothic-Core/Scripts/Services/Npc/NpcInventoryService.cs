using System.Collections.Generic;
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
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        // VmService leads back to this service through its own dependencies - resolved on first use.
        private VmService _vmService;
        private VmService VmService => _vmService ??= ReflexProjectInstaller.DIContainer.Resolve<VmService>();

        /// <summary>
        /// Like the engine (OpenGothic Inventory::use): the item's use function (on_state[0]) runs with the NPC as self
        /// and the item as item - a potion heals, food feeds, a letter or map is shown (Doc_*). The caller takes care of
        /// the item itself (eaten or kept).
        /// </summary>
        public void UseItem(NpcContainer npc, ItemInstance item)
        {
            var function = item.GetOnState(ItemStateSlot.Slot0);
            if (function <= 0 || npc?.Instance == null)
                return;

            var vm = _gameStateService.GothicVm;
            var oldItem = vm.GlobalItem;
            vm.GlobalItem = item;
            try
            {
                Logger.Log($"[NpcInventory] {npc.Instance.GetName(NpcNameSlot.Slot0)} uses {item.Name}", LogCat.Npc);
                VmService.CallAsNpc(function, npc.Instance, null, null, LogCat.Npc);
            }
            finally
            {
                vm.GlobalItem = oldItem;
            }
        }

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
            var props = npc.GetUserData().Props;
            var itemData = _vmCacheService.TryGetItemData(itemId);
            
            props.EquippedItems.Add(itemData);
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
                        items.RemoveAt(i);
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
