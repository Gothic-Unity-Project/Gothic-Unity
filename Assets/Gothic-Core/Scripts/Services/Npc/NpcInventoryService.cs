using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using ZenKit.Daedalus;
using static Gothic.Core.Models.Vm.VmGothicEnums;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Services.Npc
{
    public class NpcInventoryService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        // on_equip/on_unequip of script equips, run after the current instance initialization finished.
        private readonly List<(NpcInstance npc, ItemInstance item, int function)> _deferredItemFunctions = new();

        
        public void ExtEquipItem(NpcInstance npc, int itemId)
        {
            var itemData = _vmCacheService.TryGetItemData(itemId);

            // "gibt dem Nsc direkt das angegebene Item an den Guertel"
            // (externals.d) - the engine gives the item too. Orc shamans equip their staff without CreateInvItem,
            // Mroczne Tajemnice's Rockefeller his warhammer: equipped, but not in the inventory.
            if (itemData != null && ExtNpcHasItems(npc, itemId) <= 0)
            {
                ExtCreateInvItems(npc, itemId, 1);
                Logger.Log($"[Equip] {npc.GetName(NpcNameSlot.Slot0)} got '{itemData.Name}' from EquipItem " +
                           "(not in the inventory)", LogCat.Npc);
            }

            // The engine applies protection[] + on_equip for script equips too (OpenGothic: Inventory::setSlot -> applyArmor).
            // Without it, every NPC's armor (and the hero's starting armor) protected nothing.
            // A mod's EquipItem with a symbol that's no item: nothing to equip (a null entry broke every view).
            if (itemData == null)
            {
                Logger.LogWarning($"[Equip] {npc.GetName(NpcNameSlot.Slot0)}: EquipItem symbol {itemId} is no item",
                    LogCat.Npc);
                return;
            }

            EquipItemWithEffects(npc, itemData, deferItemFunctions: true);
        }

        /// <summary>
        /// Equip like the engine does: free the slot first (armor, amulet, belt, rings),
        /// add the item's protection[] values and call its on_equip (e.g. amulet attribute bonuses).
        /// </summary>
        /// <param name="deferItemFunctions">
        /// TRUE for equips coming from Daedalus externals (EquipItem, Mdl_SetVisualBody). They run *inside* an instance
        /// initialization - calling on_equip there nests a VM call into the running constructor (e.g. with GlobalHero not
        /// set yet). A failing nested call aborted the whole constructor: PC_Rockefeller lost all talents and items.
        /// The functions run next frame instead. Save/load: that's before saved attributes are restored, which overwrite them.
        /// </param>
        public void EquipItemWithEffects(NpcInstance npc, ItemInstance item, bool deferItemFunctions = false)
        {
            var props = npc.GetUserData().Props;

            foreach (var conflicting in GetConflictingEquippedItems(props.EquippedItems, item).ToList())
                UnequipItemWithEffects(npc, conflicting, deferItemFunctions, updateArmorVisual: false);

            props.EquippedItems.Add(item);
            ApplyItemProtection(npc, item, 1);
            RunItemFunction(npc, item, item.OnEquip, deferItemFunctions);

            if (IsHelmet(item))
                RefreshHelmetVisual(npc);
            else if (IsArmor(item))
                UpdateArmorVisual(npc, item);

            Logger.Log($"[Equip] {npc.GetName(NpcNameSlot.Slot0)} equipped '{item.Name}'", LogCat.Npc);
        }

        /// <summary>
        /// Counterpart of EquipItemWithEffects(). Returns false if the item wasn't equipped.
        /// </summary>
        public bool UnequipItemWithEffects(NpcInstance npc, ItemInstance item, bool deferItemFunctions = false,
            bool updateArmorVisual = true)
        {
            var props = npc.GetUserData().Props;
            var equipped = props.EquippedItems.FirstOrDefault(i => i.Index == item.Index);
            if (equipped == null)
                return false;

            props.EquippedItems.Remove(equipped);
            ApplyItemProtection(npc, equipped, -1);
            RunItemFunction(npc, equipped, equipped.OnUnEquip, deferItemFunctions);

            if (IsHelmet(equipped))
                RefreshHelmetVisual(npc);
            else if (updateArmorVisual && IsArmor(equipped))
                UpdateArmorVisual(npc, null);

            Logger.Log($"[Equip] {npc.GetName(NpcNameSlot.Slot0)} unequipped '{equipped.Name}'", LogCat.Npc);
            return true;
        }

        /// <summary>
        /// AI_EquipArmor: equips an armor the NPC carries (engine: nothing happens without it in the inventory).
        /// </summary>
        public void EquipArmorFromInventory(NpcInstance npc, ItemInstance armor)
        {
            if (armor == null)
                return;

            if (ExtNpcHasItems(npc, armor.Index) <= 0)
            {
                Logger.LogWarning($"[ArmorVisual] {npc.GetName(NpcNameSlot.Slot0)} doesn't carry '{armor.Name}' - " +
                                  "AI_EquipArmor ignored like in the engine.", LogCat.Npc);
                return;
            }

            if (npc.GetUserData().Props.EquippedItems.Any(i => i.Index == armor.Index))
                return;

            EquipItemWithEffects(npc, armor);
        }

        /// <summary>
        /// AI_UnequipArmor: takes the armor off (it stays in the inventory), the NPC shows its naked body.
        /// </summary>
        public void UnequipArmor(NpcInstance npc)
        {
            var armor = npc.GetUserData().Props.EquippedItems.FirstOrDefault(IsTorsoArmor);
            if (armor != null)
                UnequipItemWithEffects(npc, armor);
        }

        /// <summary>
        /// AI_EquipBestArmor: the carried armor with the highest total protection.
        /// </summary>
        public void EquipBestArmor(NpcInstance npc)
        {
            ItemInstance best = null;
            var bestProtection = int.MinValue;
            foreach (var content in GetAllInventoryItems(npc))
            {
                var item = _vmCacheService.TryGetItemData(content.Name);
                if (!IsTorsoArmor(item))
                    continue;

                var protection = 0;
                for (var i = 0; i < 8; i++)
                    protection += item.GetProtection((DamageType)i);

                if (protection > bestProtection)
                {
                    best = item;
                    bestProtection = protection;
                }
            }

            EquipArmorFromInventory(npc, best);
        }

        private static bool IsArmor(ItemInstance item)
        {
            return item != null && ((ItemFlags)item.MainFlag & ItemFlags.ItemKatArmor) != 0;
        }

        /// <summary>
        /// C_ITEM.wear = WEAR_HEAD (2): a helmet - its own slot next to the body armor (WEAR_TORSO = 1), e.g. MT's HELM,
        /// PALHELM. The original engine attaches its visual to the head instead of swapping the body.
        /// </summary>
        public static bool IsHelmet(ItemInstance item)
        {
            return IsArmor(item) && (item.Wear & _wearHead) != 0;
        }

        public static bool IsTorsoArmor(ItemInstance item)
        {
            return IsArmor(item) && (item.Wear & _wearHead) == 0;
        }

        private const int _wearHead = 2;
        private const string _helmetHolderName = "_EquippedHelmet";
        private static readonly string[] _helmetSlotNames = { "ZS_HELMET", "BIP01 HEAD" };

        /// <summary>
        /// Shows the equipped helmet (if any) on the NPC's head. Idempotent - also used after (lazy) mesh builds and loads.
        /// </summary>
        public void RefreshHelmetVisual(NpcInstance npc)
        {
            var container = npc.GetUserData();
            if (container?.Go == null || container.Vob == null || container.Vob.Player)
                return;

            var oldHolder = container.Go.FindChildRecursively(_helmetHolderName);
            if (oldHolder != null)
                Object.Destroy(oldHolder);

            var helmet = container.Props.EquippedItems.FirstOrDefault(IsHelmet);
            if (helmet == null || string.IsNullOrEmpty(helmet.Visual))
                return;

            GameObject slot = null;
            foreach (var slotName in _helmetSlotNames)
            {
                slot = container.Go.FindChildRecursively(slotName);
                if (slot != null)
                    break;
            }
            if (slot == null)
            {
                Logger.LogWarning($"[ArmorVisual] No head slot on {npc.GetName(NpcNameSlot.Slot0)} for '{helmet.Visual}'.", LogCat.Npc);
                return;
            }

            var holder = new GameObject(_helmetHolderName);
            holder.transform.SetParent(slot.transform, false);
            _vobService.CreateItemMesh(helmet.Index, holder);
            Logger.Log($"[ArmorVisual] {npc.GetName(NpcNameSlot.Slot0)} wears helmet '{helmet.Visual}' at {slot.name}", LogCat.Npc);
        }

        /// <summary>
        /// The engine swaps the body mesh to the armor's visual_change (or back
        /// to the naked body) whenever an armor is (un)equipped - Greg putting on Lobart's clothes, Pedro's novice robe,
        /// Cavalorn's disguise... Not built yet (lazy loading): InitNpc picks up MdmName later.
        /// Save/load: equipped items are saved; NpcService.RestoreEquipment re-derives MdmName from them.
        /// The VR hero has no Gothic body - only protection/effects apply.
        /// </summary>
        public void UpdateArmorVisual(NpcInstance npc, ItemInstance armorOrNull)
        {
            if (IsHelmet(armorOrNull))
                return;

            var container = npc.GetUserData();
            // The VR hero has no Gothic body - except the VR body (EnableVrHeroBody), which follows props.MdmName.
            if (container == null || container.Vob == null || (container.Vob.Player && !_configService.Dev.EnableVrHeroBody))
                return;

            var props = container.Props;
            if (string.IsNullOrEmpty(props.BodyData.Body))
                return; // Mdl_SetVisualBody not called yet - it sets the visual itself.

            var newMdm = !string.IsNullOrEmpty(armorOrNull?.VisualChange) ? armorOrNull.VisualChange : props.BodyData.Body;
            props.BodyData.Armor = armorOrNull?.Index ?? -1;
            if (newMdm.EqualsIgnoreCase(props.MdmName))
                return;

            props.MdmName = newMdm;

            // The VR hero body rebuilds itself from MdmName. An NPC's built mesh is swapped with a later PR (meshes).
            Logger.Log($"[ArmorVisual] {npc.GetName(NpcNameSlot.Slot0)} now wears '{newMdm}'", LogCat.Npc);
        }

        /// <summary>
        /// An equipped weapon taken from an NPC (loot, NPC backpack): off the equipped list and its mesh off the body
        /// (holster slot, or the hand for NPCs that went down mid-draw).
        /// </summary>
        public void RemoveEquippedWeapon(NpcContainer npc, string itemInstanceName)
        {
            var item = _vmCacheService.TryGetItemData(itemInstanceName);
            var equipped = item == null ? null : npc.Props.EquippedItems.FirstOrDefault(i => i.Index == item.Index);
            if (equipped == null)
                return;

            npc.Props.EquippedItems.Remove(equipped);

            var mainFlag = (ItemFlags)equipped.MainFlag;
            var flags = (ItemFlags)equipped.Flags;
            string holsterSlot;
            if (mainFlag == ItemFlags.ItemKatNf)
                holsterSlot = flags.HasFlag(ItemFlags.Item2HdAxe) || flags.HasFlag(ItemFlags.Item2HdSwd)
                    ? "ZS_LONGSWORD" : "ZS_SWORD";
            else if (mainFlag == ItemFlags.ItemKatFf)
                holsterSlot = flags.HasFlag(ItemFlags.ItemCrossbow) ? "ZS_CROSSBOW" : "ZS_BOW";
            else
                return;

            foreach (var slotName in new[] { holsterSlot, "ZS_RIGHTHAND" })
            {
                var slotGo = npc.Go.FindChildRecursively(slotName);
                if (slotGo == null || slotGo.transform.childCount == 0)
                    continue;
                Object.Destroy(slotGo.transform.GetChild(0).gameObject);
                return;
            }
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
            // Helmets (WEAR_HEAD) and body armor (WEAR_TORSO) are separate slots.
            if (((ItemFlags)item.MainFlag & ItemFlags.ItemKatArmor) != 0)
                return equippedItems.Where(i => IsArmor(i) && IsHelmet(i) == IsHelmet(item));

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

        private void RunItemFunction(NpcInstance npc, ItemInstance item, int function, bool defer)
        {
            if (function <= 0)
                return;

            if (!defer)
            {
                CallItemFunction(npc, item, function);
                return;
            }

            _deferredItemFunctions.Add((npc, item, function));
            if (_deferredItemFunctions.Count == 1)
                _unityMonoService.StartCoroutine(RunDeferredItemFunctions());
        }

        private IEnumerator RunDeferredItemFunctions()
        {
            yield return null;

            var pending = _deferredItemFunctions.ToList();
            _deferredItemFunctions.Clear();
            foreach (var (npc, item, function) in pending)
                CallItemFunction(npc, item, function);
        }

        /// <summary>
        /// The NPC whose item function (on_equip/on_unequip) runs right now, null otherwise.
        /// </summary>
        public NpcInstance ItemFunctionOwner { get; private set; }

        private void CallItemFunction(NpcInstance npc, ItemInstance item, int function)
        {
            if (function <= 0)
                return;

            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            var oldOwner = ItemFunctionOwner;
            vm.GlobalSelf = npc;
            vm.GlobalItem = item;
            ItemFunctionOwner = npc;
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
                ItemFunctionOwner = oldOwner;
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

            // DeveloperConfig.EnableItemInitSymbolCheck: no item (G2's ItemHoshi: ITWR_ADDON_LPBOOK is a prototype) -
            // skipped like the engine. The exception stopped the NPC's creation.
            var itemData = _vmCacheService.TryGetItemData(itemIndex);
            if (itemData == null)
            {
                Logger.LogWarning($"[CreateInvItems] {npc.GetName(NpcNameSlot.Slot0)}: '{itemInstance?.Name}' is no " +
                                  "item - skipped.", LogCat.Npc);
                return;
            }

            var mainFlag = (VmGothicEnums.ItemFlags)itemData.MainFlag;
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

            var itemData = _vmCacheService.TryGetItemData(itemIndex);
            if (itemData == null)
                return;
            var mainFlag = (VmGothicEnums.ItemFlags)itemData.MainFlag;
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
    }
}
