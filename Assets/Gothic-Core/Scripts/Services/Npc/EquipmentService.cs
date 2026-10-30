using System.Collections;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Vm;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Wearing armor, amulets, rings and belts like the engine (OpenGothic Inventory::setSlot): the item's conditions
    /// (cond_atr/cond_value) must be met, one armor, amulet and belt and two rings at most - a new armor, amulet or belt
    /// replaces the old one. Wearing adds the item's protection and runs its on_equip, taking it off the reverse and
    /// on_unequip. Weapons are handled by the hands (VR) and the fight code.
    /// </summary>
    public class EquipmentService
    {
        private const int _maxRings = 2;
        private const int _protectionCount = 8;
        private const int _attributeCount = 8;
        // VR moves an item between hand and backpack by taking it out of the inventory and back in. Only an item that
        // stays gone this long is taken off.
        private const float _goneCheckSeconds = 0.5f;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        // VmService leads back to this service through its own dependencies - resolved on first use.
        private VmService _vmService;
        private VmService VmService => _vmService ??= ReflexProjectInstaller.DIContainer.Resolve<VmService>();

        public static bool IsWearable(ItemInstance item)
        {
            var mainFlag = (VmGothicEnums.ItemFlags)item.MainFlag;
            var flags = (VmGothicEnums.ItemFlags)item.Flags;
            return mainFlag.HasFlag(VmGothicEnums.ItemFlags.ItemKatArmor) ||
                   (flags & (VmGothicEnums.ItemFlags.ItemAmulet | VmGothicEnums.ItemFlags.ItemRing |
                             VmGothicEnums.ItemFlags.ItemBelt)) != 0;
        }

        public bool IsEquipped(NpcContainer npc, ItemInstance item)
        {
            return npc.Props.EquippedItems.Any(i => i.Index == item.Index);
        }

        /// <summary>
        /// Puts it on, or takes it off if worn already.
        /// </summary>
        /// <returns>True if it changed.</returns>
        public bool ToggleEquip(NpcContainer npc, ItemInstance item)
        {
            return IsEquipped(npc, item) ? Unequip(npc, item) : Equip(npc, item);
        }

        public bool Equip(NpcContainer npc, ItemInstance item)
        {
            if (!IsWearable(item) || IsEquipped(npc, item) || !MeetsConditions(npc, item))
                return false;

            var flags = (VmGothicEnums.ItemFlags)item.Flags;
            if (flags.HasFlag(VmGothicEnums.ItemFlags.ItemRing))
            {
                if (npc.Props.EquippedItems.Count(IsRing) >= _maxRings)
                {
                    Logger.Log($"[Equipment] {item.Name}: both ring fingers are taken", LogCat.Npc);
                    return false;
                }
            }
            else
            {
                var worn = npc.Props.EquippedItems.FirstOrDefault(i => IsSameSlot(i, item));
                if (worn != null)
                    Unequip(npc, worn);
            }

            npc.Props.EquippedItems.Add(item);
            ChangeProtection(npc, item, 1);
            CallItemFunction(npc, item, item.OnEquip);
            Logger.Log($"[Equipment] {npc.Instance.GetName(NpcNameSlot.Slot0)} wears {item.Name}", LogCat.Npc);
            return true;
        }

        /// <summary>
        /// The scripts' EquipItem: like the engine, it puts the item on without checking its conditions (an NPC's armor
        /// protects it, found on BoroBongo's branch). While the NPC's instance is built (no object yet), only the
        /// instance gets the protection: a new NPC copies it to its vob, a loaded one keeps its saved values - added
        /// again they would count twice. Running on_equip inside the instance's constructor is left out then.
        /// </summary>
        public void EquipByScript(NpcContainer npc, ItemInstance item)
        {
            if (npc?.Props == null || item == null)
                return;

            if (!IsWearable(item))
            {
                npc.Props.EquippedItems.Add(item);
                return;
            }

            if (IsEquipped(npc, item))
                return;

            var isBeingBuilt = npc.Go == null;
            if (!IsRing(item))
            {
                var worn = npc.Props.EquippedItems.FirstOrDefault(i => IsSameSlot(i, item));
                if (worn != null)
                {
                    if (isBeingBuilt)
                        npc.Props.EquippedItems.Remove(worn);
                    else
                        Unequip(npc, worn);
                }
            }

            npc.Props.EquippedItems.Add(item);
            if (isBeingBuilt)
            {
                ChangeInstanceProtection(npc, item);
                return;
            }

            ChangeProtection(npc, item, 1);
            CallItemFunction(npc, item, item.OnEquip);
        }

        public bool Unequip(NpcContainer npc, ItemInstance item)
        {
            var worn = npc.Props.EquippedItems.FirstOrDefault(i => i.Index == item.Index);
            if (worn == null || !IsWearable(worn))
                return false;

            npc.Props.EquippedItems.Remove(worn);
            ChangeProtection(npc, worn, -1);
            CallItemFunction(npc, worn, worn.OnUnEquip);
            Logger.Log($"[Equipment] {npc.Instance.GetName(NpcNameSlot.Slot0)} takes off {worn.Name}", LogCat.Npc);
            return true;
        }

        /// <summary>
        /// The item left the inventory (NpcInventoryService): like the engine, what isn't carried any more isn't worn.
        /// </summary>
        public void OnItemGone(NpcContainer npc, ItemInstance item, System.Func<bool> isStillGone)
        {
            if (npc?.Props == null || !IsEquipped(npc, item) || !IsWearable(item))
                return;

            _unityMonoService.StartCoroutine(UnequipIfStillGone(npc, item, isStillGone));
        }

        private IEnumerator UnequipIfStillGone(NpcContainer npc, ItemInstance item, System.Func<bool> isStillGone)
        {
            yield return new WaitForSeconds(_goneCheckSeconds);
            if (isStillGone())
                Unequip(npc, item);
        }

        /// <summary>
        /// The item's attribute conditions, e.g. strength 30 for a heavy armor (cond_atr/cond_value).
        /// </summary>
        public bool MeetsConditions(NpcContainer npc, ItemInstance item)
        {
            foreach (var slot in new[] { ItemConditionSlot.Slot0, ItemConditionSlot.Slot1, ItemConditionSlot.Slot2 })
            {
                var value = item.GetCondValue(slot);
                var attribute = item.GetCondAtr(slot);
                if (value != 0 && npc.Vob.GetAttribute(attribute) < value)
                {
                    Logger.Log($"[Equipment] {item.Name} needs {value} of attribute {attribute}", LogCat.Npc);
                    PrintCannotUse(npc, attribute, value);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Like the engine, the scripts tell the player what's missing (G_CanNotUse: "You need 30 strength").
        /// </summary>
        private void PrintCannotUse(NpcContainer npc, int attribute, int value)
        {
            var vm = _gameStateService.GothicVm;
            var function = vm.GetSymbolByName("G_CanNotUse");
            var isHero = npc.Instance.Index == (vm.GlobalHero as NpcInstance)?.Index;
            if (function == null || !isHero)
                return;

            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = npc.Instance;
            try
            {
                vm.Call(function.Index, 1, attribute, value);
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"[Equipment] G_CanNotUse failed: {e.Message}", LogCat.Npc);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }
        }

        private static bool IsRing(ItemInstance item)
        {
            return ((VmGothicEnums.ItemFlags)item.Flags).HasFlag(VmGothicEnums.ItemFlags.ItemRing);
        }

        private static bool IsSameSlot(ItemInstance worn, ItemInstance item)
        {
            if (((VmGothicEnums.ItemFlags)item.MainFlag).HasFlag(VmGothicEnums.ItemFlags.ItemKatArmor))
                return ((VmGothicEnums.ItemFlags)worn.MainFlag).HasFlag(VmGothicEnums.ItemFlags.ItemKatArmor);

            var slotFlags = VmGothicEnums.ItemFlags.ItemAmulet | VmGothicEnums.ItemFlags.ItemBelt;
            var itemSlot = (VmGothicEnums.ItemFlags)item.Flags & slotFlags;
            return itemSlot != 0 && ((VmGothicEnums.ItemFlags)worn.Flags & slotFlags) == itemSlot;
        }

        /// <summary>
        /// Like the engine, the item's protection adds to the wearer's (vob and instance, which the scripts read).
        /// </summary>
        private static void ChangeProtection(NpcContainer npc, ItemInstance item, int sign)
        {
            for (var i = 0; i < _protectionCount; i++)
            {
                var change = item.GetProtection((DamageType)i) * sign;
                if (change == 0)
                    continue;

                var value = npc.Vob.GetProtection(i) + change;
                npc.Vob.SetProtection(i, value);
                npc.Instance.SetProtection((DamageType)i, value);
            }
        }

        private static void ChangeInstanceProtection(NpcContainer npc, ItemInstance item)
        {
            for (var i = 0; i < _protectionCount; i++)
            {
                var change = item.GetProtection((DamageType)i);
                if (change != 0)
                    npc.Instance.SetProtection((DamageType)i, npc.Instance.GetProtection((DamageType)i) + change);
            }
        }

        private void CallItemFunction(NpcContainer npc, ItemInstance item, int function)
        {
            if (function <= 0)
                return;

            var vm = _gameStateService.GothicVm;
            var oldItem = vm.GlobalItem;
            vm.GlobalItem = item;
            try
            {
                VmService.CallAsNpc(function, npc.Instance, null, null, LogCat.Npc);
            }
            finally
            {
                vm.GlobalItem = oldItem;
            }

            CopyStatsFromInstance(npc);
        }

        /// <summary>
        /// The item's script changes the instance (self.protection[], self.attribute[]) - the vob keeps the same
        /// values for C# (damage, health bars).
        /// </summary>
        private static void CopyStatsFromInstance(NpcContainer npc)
        {
            for (var i = 0; i < _protectionCount; i++)
                npc.Vob.SetProtection(i, npc.Instance.GetProtection((DamageType)i));

            for (var i = 0; i < _attributeCount; i++)
            {
                var value = npc.Instance.GetAttribute((NpcAttribute)i);
                if (npc.Vob.GetAttribute(i) == value)
                    continue;

                npc.Vob.SetAttribute(i, value);
                GlobalEventDispatcher.NpcAttributeChanged.Invoke(npc, (NpcAttribute)i);
            }
        }
    }
}
