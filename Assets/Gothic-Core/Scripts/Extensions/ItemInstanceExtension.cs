using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Models.Vob;
using ZenKit.Daedalus;
using static Gothic.Core.Models.Vm.VmGothicEnums;

namespace Gothic.Core.Extensions
{
    /// <summary>
    /// Item rules of the engine (equip slots), based on C_ITEM's flags and wear fields.
    /// </summary>
    public static class ItemInstanceExtension
    {
        // C_ITEM.wear: WEAR_TORSO = 1, WEAR_HEAD = 2
        private const int _wearHead = 2;


        /// <summary>
        /// A melee weapon (ITEM_KAT_NF: swords, axes, clubs) - what can be swung, blocked with and stabbed. The one
        /// definition of "melee weapon"; never derive it from the mesh name.
        /// </summary>
        public static bool IsMeleeWeapon(this ItemInstance item)
        {
            return item != null && ((ItemFlags)item.MainFlag & ItemFlags.ItemKatNf) != 0;
        }

        /// <summary>
        /// A ranged weapon (ITEM_KAT_FF: bows and crossbows).
        /// </summary>
        public static bool IsRangedWeapon(this ItemInstance item)
        {
            return item != null && ((ItemFlags)item.MainFlag & ItemFlags.ItemKatFf) != 0;
        }

        /// <summary>
        /// A crossbow (ITEM_CROSSBOW) - a ranged weapon that isn't one is a bow.
        /// </summary>
        public static bool IsCrossbow(this ItemInstance item)
        {
            return item != null && ((ItemFlags)item.Flags & ItemFlags.ItemCrossbow) != 0;
        }

        /// <summary>
        /// Bow (ITEM_KAT_FF without ITEM_CROSSBOW), CBow (ITEM_CROSSBOW) or NoWeapon (no ranged weapon). The one
        /// place that tells which items are bows and crossbows: NPCs drawing them (AiDrawWeapon), their colliders
        /// (GetColliderKind) and the VR hero's ranged components and weapon state.
        /// </summary>
        public static WeaponState GetRangedWeaponState(this ItemInstance item)
        {
            if (!item.IsRangedWeapon())
                return WeaponState.NoWeapon;

            return item.IsCrossbow() ? WeaponState.CBow : WeaponState.Bow;
        }

        /// <summary>
        /// Bows and crossbows get colliders fitted to their parts, melee weapons (IsMeleeWeapon) too while
        /// pre-caching, everything else (arrows and bolts too) sliced ones. A ranged flag wins over a melee one.
        /// </summary>
        public static ItemColliderKind GetColliderKind(this ItemInstance item)
        {
            return item.GetRangedWeaponState() switch
            {
                WeaponState.Bow => ItemColliderKind.Bow,
                WeaponState.CBow => ItemColliderKind.Crossbow,
                _ => item.IsMeleeWeapon() ? ItemColliderKind.Melee : ItemColliderKind.Default
            };
        }

        public static bool IsArmor(this ItemInstance item)
        {
            return item != null && ((ItemFlags)item.MainFlag & ItemFlags.ItemKatArmor) != 0;
        }

        /// <summary>
        /// C_ITEM.wear = WEAR_HEAD (2): a helmet - its own slot next to the body armor (WEAR_TORSO = 1), e.g. MT's
        /// HELM, PALHELM. The original engine attaches its visual to the head instead of swapping the body.
        /// </summary>
        public static bool IsHelmet(this ItemInstance item)
        {
            return item.IsArmor() && (item.Wear & _wearHead) != 0;
        }

        public static bool IsTorsoArmor(this ItemInstance item)
        {
            return item.IsArmor() && (item.Wear & _wearHead) == 0;
        }

        /// <summary>
        /// Items with a body slot: one armor, one amulet, one belt, two rings.
        /// </summary>
        public static bool IsWearable(this ItemInstance item)
        {
            return item != null &&
                   (((ItemFlags)item.MainFlag & ItemFlags.ItemKatArmor) != 0 ||
                    ((ItemFlags)item.Flags & (ItemFlags.ItemAmulet | ItemFlags.ItemRing | ItemFlags.ItemBelt)) != 0);
        }

        /// <summary>
        /// Weapons (ITEM_KAT_NF, ITEM_KAT_FF) stay equipped outside the backpack, like Gothic: they come off when
        /// dropped, not when taken into a hand or holster. Everything else comes off with the last one leaving the
        /// inventory.
        /// </summary>
        public static bool StaysEquippedOutsideBackpack(this ItemInstance item)
        {
            return item.IsMeleeWeapon() || item.IsRangedWeapon();
        }

        /// <summary>
        /// The equipped items that have to leave their slot before this item can be equipped.
        /// </summary>
        public static IEnumerable<ItemInstance> GetEquipConflicts(this ItemInstance item,
            List<ItemInstance> equippedItems)
        {
            // Helmets (WEAR_HEAD) and body armor (WEAR_TORSO) are separate slots.
            if (((ItemFlags)item.MainFlag & ItemFlags.ItemKatArmor) != 0)
                return equippedItems.Where(i => i.IsArmor() && i.IsHelmet() == item.IsHelmet());

            if (((ItemFlags)item.Flags & ItemFlags.ItemAmulet) != 0)
                return equippedItems.Where(i => ((ItemFlags)i.Flags & ItemFlags.ItemAmulet) != 0);

            if (((ItemFlags)item.Flags & ItemFlags.ItemBelt) != 0)
                return equippedItems.Where(i => ((ItemFlags)i.Flags & ItemFlags.ItemBelt) != 0);

            // One melee weapon and one bow/crossbow (Gothic's slots) - several stayed equipped (the backpack showed [E]
            // on each in turn).
            if (((ItemFlags)item.MainFlag & ItemFlags.ItemKatNf) != 0)
                return equippedItems.Where(i => ((ItemFlags)i.MainFlag & ItemFlags.ItemKatNf) != 0);
            if (((ItemFlags)item.MainFlag & ItemFlags.ItemKatFf) != 0)
                return equippedItems.Where(i => ((ItemFlags)i.MainFlag & ItemFlags.ItemKatFf) != 0);

            if (((ItemFlags)item.Flags & ItemFlags.ItemRing) != 0)
            {
                // Two ring slots: free the oldest one only when both are taken.
                var rings = equippedItems.Where(i => ((ItemFlags)i.Flags & ItemFlags.ItemRing) != 0).ToList();
                return rings.Count >= 2 ? rings.Take(1) : Enumerable.Empty<ItemInstance>();
            }

            return Enumerable.Empty<ItemInstance>();
        }

        /// <summary>
        /// Runes are all called "Runa"/"Rune" - the spell is their description, like in the backpack.
        /// </summary>
        public static string GetSpellName(this ItemInstance item)
        {
            return string.IsNullOrEmpty(item?.Description) ? item?.Name : item.Description;
        }
    }
}
