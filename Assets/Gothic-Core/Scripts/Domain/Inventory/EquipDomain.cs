using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Domain.Meshes.Builder;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Inventory;
using Reflex.Attributes;
using ZenKit.Daedalus;
using static Gothic.Core.Models.Vm.VmGothicEnums;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Inventory
{
    /// <summary>
    /// Equipping like the engine: body slots (armor, helmet, amulet, belt, rings), protection[] and
    /// on_equip/on_unequip, plus the visuals that follow (armor MdmName, helmet mesh). Owned by NpcInventoryService.
    /// </summary>
    public class EquipDomain
    {
        [Inject] private readonly ItemUseService _itemUseService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        private readonly NpcArmorMeshBuilder _armorMeshBuilder = new NpcArmorMeshBuilder().Inject();

        // on_equip/on_unequip of script equips, run after the current instance initialization finished.
        private readonly List<(NpcInstance npc, ItemInstance item, int function)> _deferredItemFunctions = new();

        /// <summary>
        /// The NPC whose item function (on_equip/on_unequip) runs right now, null otherwise.
        /// </summary>
        public NpcInstance ItemFunctionOwner { get; private set; }


        /// <summary>
        /// Equip like the engine does: free the slot first (armor, amulet, belt, rings),
        /// add the item's protection[] values and call its on_equip (e.g. amulet attribute bonuses).
        /// </summary>
        /// <param name="deferItemFunctions">
        /// TRUE for equips coming from Daedalus externals (EquipItem, Mdl_SetVisualBody). They run *inside* an instance
        /// initialization - calling on_equip there nests a VM call into the running constructor (e.g. with GlobalHero
        /// not set yet). A failing nested call aborted the whole constructor: PC_Rockefeller lost all talents and
        /// items. The functions run next frame instead. Save/load: that's before saved attributes are restored, which
        /// overwrite them.
        /// </param>
        public void EquipItemWithEffects(NpcInstance npc, ItemInstance item, bool deferItemFunctions = false)
        {
            var props = npc.GetUserData().Props;

            foreach (var conflicting in item.GetEquipConflicts(props.EquippedItems).ToList())
                UnequipItemWithEffects(npc, conflicting, deferItemFunctions, updateArmorVisual: false);

            props.EquippedItems.Add(item);
            ApplyItemProtection(npc, item, 1);
            RunItemFunction(npc, item, item.OnEquip, deferItemFunctions);

            if (item.IsHelmet())
                RefreshHelmetVisual(npc);
            else if (item.IsArmor())
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

            if (equipped.IsHelmet())
                RefreshHelmetVisual(npc);
            else if (updateArmorVisual && equipped.IsArmor())
                UpdateArmorVisual(npc, null);

            Logger.Log($"[Equip] {npc.GetName(NpcNameSlot.Slot0)} unequipped '{equipped.Name}'", LogCat.Npc);
            return true;
        }

        /// <summary>
        /// Shows the equipped helmet (if any) on the NPC's head. Idempotent - also used after (lazy) mesh builds and
        /// loads.
        /// </summary>
        public void RefreshHelmetVisual(NpcInstance npc)
        {
            var container = npc.GetUserData();
            if (container?.Go == null || container.Vob == null || container.Vob.Player)
                return;

            var helmet = container.Props.EquippedItems.FirstOrDefault(i => i.IsHelmet());
            _armorMeshBuilder.BuildHelmet(container, helmet);
        }

        /// <summary>
        /// The engine swaps the body mesh to the armor's visual_change (or back to the naked body) whenever an armor
        /// is (un)equipped - Greg putting on Lobart's clothes, Pedro's novice robe, Cavalorn's disguise... Not built yet (lazy loading): InitNpc picks up MdmName later.
        /// Save/load: equipped items are saved; NpcService.RestoreEquipment re-derives MdmName from them.
        /// The VR hero has no Gothic body - only protection/effects apply.
        /// </summary>
        public void UpdateArmorVisual(NpcInstance npc, ItemInstance armorOrNull)
        {
            if (armorOrNull.IsHelmet())
                return;

            var container = npc.GetUserData();
            // The VR hero has no Gothic body - except the VR body (EnableVrHeroBody), which follows props.MdmName.
            if (container == null || container.Vob == null ||
                (container.Vob.Player && !_configService.Dev.EnableVrHeroBody))
                return;

            var props = container.Props;
            if (string.IsNullOrEmpty(props.BodyData.Body))
                return; // Mdl_SetVisualBody not called yet - it sets the visual itself.

            var newMdm = !string.IsNullOrEmpty(armorOrNull?.VisualChange)
                ? armorOrNull.VisualChange
                : props.BodyData.Body;
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
        public void RemoveEquippedWeapon(NpcContainer npc, ItemInstance item)
        {
            var equipped = item == null ? null : npc.Props.EquippedItems.FirstOrDefault(i => i.Index == item.Index);
            if (equipped == null)
                return;

            npc.Props.EquippedItems.Remove(equipped);
            _armorMeshBuilder.DestroyWeaponMesh(npc, equipped);
        }

        /// <summary>
        /// Dual write (vob + instance) like ExtNpcChangeAttribute, otherwise CopyFromInstanceData would restore old
        /// values.
        /// </summary>
        private static void ApplyItemProtection(NpcInstance npc, ItemInstance item, int sign)
        {
            // The Vob can be missing while the NPC's instance is still being initialized. The instance is the source
            // then (NpcProxy.CopyFromInstanceData copies it over afterward).
            var vob = npc.GetUserData()?.Vob;
            for (var i = 0; i < Enum.GetNames(typeof(DamageType)).Length; i++)
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
            {
                if (!IsNpcInWorld(npc))
                {
                    Logger.LogWarning($"[Equip] Deferred item function {function} of '{item.Name}' skipped - its " +
                                      "NPC is no longer in the world (world change, Wld_RemoveNpc).", LogCat.Npc);
                    continue;
                }

                CallItemFunction(npc, item, function);
            }
        }

        /// <summary>
        /// Still in the current world: the NPC cache is cleared on a world change (the hero is re-added there, so it's
        /// always valid). NpcInstance.UserData is a weak pointer - it's null once nothing holds the container anymore.
        /// </summary>
        private bool IsNpcInWorld(NpcInstance npc)
        {
            var container = npc?.GetUserData();
            if (container == null)
                return false;

            return (container.Vob != null && container.Vob.Player) ||
                   _multiTypeCacheService.NpcCache.Contains(container);
        }

        private void CallItemFunction(NpcInstance npc, ItemInstance item, int function)
        {
            var oldOwner = ItemFunctionOwner;
            ItemFunctionOwner = npc;
            try
            {
                _itemUseService.CallItemFunction(npc, item, function);
            }
            finally
            {
                ItemFunctionOwner = oldOwner;
            }
        }
    }
}
