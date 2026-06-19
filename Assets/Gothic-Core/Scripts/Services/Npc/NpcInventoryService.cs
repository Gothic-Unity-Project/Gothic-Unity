using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
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
        [Inject] private readonly MeshService _meshService;

        
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
            var symbol = _gameStateService.GothicVm.GetSymbolByIndex(itemId);
            if (symbol == null)
                return 0;
            var itemInstanceName = symbol.Name;

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

        public void ExtAiEquipBestMeleeWeapon(NpcInstance npc)
        {
            var container = npc.GetUserData();
            if (container == null)
                return;

            // Find already-equipped melee weapon, or pick the best from inventory.
            var equipped = container.Props.EquippedItems
                .FirstOrDefault(i => i.MainFlag == (int)ItemFlags.ItemKatNf);

            if (equipped == null)
            {
                List<ContentItem> weaponItems;
                try { weaponItems = GetInventoryItems(npc, InvCats.InvWeapon); }
                catch { return; }

                var bestDamage = -1;
                foreach (var contentItem in weaponItems)
                {
                    var symbol = _gameStateService.GothicVm.GetSymbolByName(contentItem.Name);
                    if (symbol == null) continue;
                    var itemData = _vmCacheService.TryGetItemData(symbol.Index);
                    if (itemData == null || itemData.MainFlag != (int)ItemFlags.ItemKatNf) continue;
                    if (itemData.DamageTotal > bestDamage)
                    {
                        bestDamage = itemData.DamageTotal;
                        equipped = itemData;
                    }
                }

                if (equipped == null)
                {
                    Logger.LogWarning($"[AI_EquipBestMeleeWeapon] {npc.GetName(NpcNameSlot.Slot0)}: no melee weapon in inventory", LogCat.Npc);
                    return;
                }

                container.Props.EquippedItems.Add(equipped);
                Logger.Log($"[AI_EquipBestMeleeWeapon] {npc.GetName(NpcNameSlot.Slot0)}: equipped '{equipped.Name}' dmg={bestDamage}", LogCat.Npc);
            }

            // Check if the weapon mesh GO exists in the stow slot or the hand slot.
            // The mesh can be lost after combat cycles; if missing, respawn it.
            var isTwoHanded = ((ItemFlags)equipped.Flags).HasFlag(ItemFlags.Item2HdAxe) ||
                              ((ItemFlags)equipped.Flags).HasFlag(ItemFlags.Item2HdSwd);
            var stowSlotName = isTwoHanded ? "ZS_LONGSWORD" : "ZS_SWORD";

            var stowGo = container.Go.FindChildRecursively(stowSlotName);
            var handGo = container.Go.FindChildRecursively("ZS_RIGHTHAND");

            var meshExists = (stowGo != null && stowGo.transform.childCount > 0) ||
                             (handGo != null && handGo.transform.childCount > 0);

            if (!meshExists)
            {
                Logger.Log($"[AI_EquipBestMeleeWeapon] {npc.GetName(NpcNameSlot.Slot0)}: mesh missing — respawning '{equipped.Name}'", LogCat.Npc);
                _meshService.CreateNpcWeapon(container.Go, equipped, (ItemFlags)equipped.MainFlag, (ItemFlags)equipped.Flags);
            }
        }
    }
}
