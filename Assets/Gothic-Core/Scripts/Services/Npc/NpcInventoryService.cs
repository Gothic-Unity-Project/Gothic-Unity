using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
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
        [Inject] private readonly ConfigService _configService;

        
        public void ExtEquipItem(NpcInstance npc, int itemId)
        {
            var props = npc.GetUserData().Props;
            var itemData = _vmCacheService.TryGetItemData(itemId);

            // The engine applies protection[] + on_equip for script equips too (OpenGothic: Inventory::setSlot -> applyArmor).
            // Without it, every NPC's armor (and the hero's starting armor) protected nothing.
            if (_configService.Dev.EnableScriptEquipEffects && itemData != null)
            {
                EquipItemWithEffects(npc, itemData);
                return;
            }

            props.EquippedItems.Add(itemData);
        }

        /// <summary>
        /// Equip like the engine does: free the slot first (armor, amulet, belt, rings),
        /// add the item's protection[] values and call its on_equip (e.g. amulet attribute bonuses).
        /// </summary>
        public void EquipItemWithEffects(NpcInstance npc, ItemInstance item)
        {
            var props = npc.GetUserData().Props;

            foreach (var conflicting in GetConflictingEquippedItems(props.EquippedItems, item).ToList())
                UnequipItemWithEffects(npc, conflicting);

            props.EquippedItems.Add(item);
            ApplyItemProtection(npc, item, 1);
            CallItemFunction(npc, item, item.OnEquip);

            Logger.Log($"[Equip] {npc.GetName(NpcNameSlot.Slot0)} equipped '{item.Name}'", LogCat.Npc);
        }

        /// <summary>
        /// Counterpart of EquipItemWithEffects(). Returns false if the item wasn't equipped.
        /// </summary>
        public bool UnequipItemWithEffects(NpcInstance npc, ItemInstance item)
        {
            var props = npc.GetUserData().Props;
            var equipped = props.EquippedItems.FirstOrDefault(i => i.Index == item.Index);
            if (equipped == null)
                return false;

            props.EquippedItems.Remove(equipped);
            ApplyItemProtection(npc, equipped, -1);
            CallItemFunction(npc, equipped, equipped.OnUnEquip);

            Logger.Log($"[Equip] {npc.GetName(NpcNameSlot.Slot0)} unequipped '{equipped.Name}'", LogCat.Npc);
            return true;
        }

        public bool IsEquipped(NpcInstance npc, string itemInstanceName)
        {
            var item = _vmCacheService.TryGetItemData(itemInstanceName);
            return item != null && npc.GetUserData().Props.EquippedItems.Any(i => i.Index == item.Index);
        }

        /// <summary>
        /// Items with a body slot: one armor, one amulet, one belt, two rings.
        /// </summary>
        public static bool IsWearable(ItemInstance item)
        {
            return item != null &&
                   (((ItemFlags)item.MainFlag & ItemFlags.ItemKatArmor) != 0 ||
                    ((ItemFlags)item.Flags & (ItemFlags.ItemAmulet | ItemFlags.ItemRing | ItemFlags.ItemBelt)) != 0);
        }

        private static IEnumerable<ItemInstance> GetConflictingEquippedItems(List<ItemInstance> equippedItems, ItemInstance item)
        {
            if (((ItemFlags)item.MainFlag & ItemFlags.ItemKatArmor) != 0)
                return equippedItems.Where(i => ((ItemFlags)i.MainFlag & ItemFlags.ItemKatArmor) != 0);

            if (((ItemFlags)item.Flags & ItemFlags.ItemAmulet) != 0)
                return equippedItems.Where(i => ((ItemFlags)i.Flags & ItemFlags.ItemAmulet) != 0);

            if (((ItemFlags)item.Flags & ItemFlags.ItemBelt) != 0)
                return equippedItems.Where(i => ((ItemFlags)i.Flags & ItemFlags.ItemBelt) != 0);

            if (((ItemFlags)item.Flags & ItemFlags.ItemRing) != 0)
            {
                // Two ring slots: free the oldest one only when both are taken.
                var rings = equippedItems.Where(i => ((ItemFlags)i.Flags & ItemFlags.ItemRing) != 0).ToList();
                return rings.Count >= 2 ? rings.Take(1) : Enumerable.Empty<ItemInstance>();
            }

            return Enumerable.Empty<ItemInstance>();
        }

        /// <summary>
        /// Dual write (vob + instance) like ExtNpcChangeAttribute, otherwise CopyFromInstanceData would restore old values.
        /// </summary>
        private static void ApplyItemProtection(NpcInstance npc, ItemInstance item, int sign)
        {
            // The Vob can be missing while the NPC's instance is still being initialized. The instance is the source then
            // (NpcProxy.CopyFromInstanceData copies it over afterward).
            var vob = npc.GetUserData()?.Vob;
            for (var i = 0; i < System.Enum.GetNames(typeof(DamageType)).Length; i++)
            {
                var delta = item.GetProtection((DamageType)i);
                if (delta == 0)
                    continue;

                var newValue = npc.GetProtection((DamageType)i) + sign * delta;
                npc.SetProtection((DamageType)i, newValue);
                vob?.SetProtection(i, newValue);
            }
        }

        private void CallItemFunction(NpcInstance npc, ItemInstance item, int function)
        {
            if (function <= 0)
                return;

            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = npc;
            vm.GlobalItem = item;
            try
            {
                vm.Call(function);
            }
            catch (System.Exception e)
            {
                Logger.LogError($"[Equip] Item function {function} of '{item.Name}' failed: {e.Message}", LogCat.Npc);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }
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

        public void ExtAiEquipBestRangedWeapon(NpcInstance npc)
        {
            if (!_configService.Dev.EnableNpcRangedCombat)
            {
                ExtAiEquipBestMeleeWeapon(npc);
                return;
            }

            var container = npc.GetUserData();
            if (container == null)
                return;

            var equipped = container.Props.EquippedItems
                .FirstOrDefault(i => i.MainFlag == (int)ItemFlags.ItemKatFf);

            if (equipped == null)
            {
                List<ContentItem> weaponItems;
                try { weaponItems = GetInventoryItems(npc, InvCats.InvWeapon); }
                catch { return; }

                foreach (var contentItem in weaponItems)
                {
                    var symbol = _gameStateService.GothicVm.GetSymbolByName(contentItem.Name);
                    if (symbol == null) continue;
                    var itemData = _vmCacheService.TryGetItemData(symbol.Index);
                    if (itemData == null || itemData.MainFlag != (int)ItemFlags.ItemKatFf) continue;
                    equipped = itemData;
                    break; // first ranged weapon found is good enough (no damage comparison for ranged)
                }

                if (equipped == null)
                {
                    Logger.LogWarning($"[AI_EquipBestRangedWeapon] {npc.GetName(NpcNameSlot.Slot0)}: no ranged weapon in inventory", LogCat.Npc);
                    return;
                }

                container.Props.EquippedItems.Add(equipped);
                Logger.Log($"[AI_EquipBestRangedWeapon] {npc.GetName(NpcNameSlot.Slot0)}: equipped '{equipped.Name}'", LogCat.Npc);
            }

            // Ensure weapon mesh exists in the bow/crossbow stow slot.
            var isCrossbow = ((ItemFlags)equipped.Flags).HasFlag(ItemFlags.ItemCrossbow);
            var stowSlotName = isCrossbow ? "ZS_CROSSBOW" : "ZS_BOW";
            var stowGo = container.Go.FindChildRecursively(stowSlotName);
            var handGo = container.Go.FindChildRecursively("ZS_LEFTHAND");

            var meshExists = (stowGo != null && stowGo.transform.childCount > 0) ||
                             (handGo != null && handGo.transform.childCount > 0);

            if (!meshExists)
            {
                Logger.Log($"[AI_EquipBestRangedWeapon] {npc.GetName(NpcNameSlot.Slot0)}: mesh missing — respawning '{equipped.Name}'", LogCat.Npc);
                _meshService.CreateNpcWeapon(container.Go, equipped, (ItemFlags)equipped.MainFlag, (ItemFlags)equipped.Flags);
            }
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
