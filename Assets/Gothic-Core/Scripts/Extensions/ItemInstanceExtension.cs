using System.Collections.Generic;
using System.Linq;
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

            if (((ItemFlags)item.Flags & ItemFlags.ItemRing) != 0)
            {
                // Two ring slots: free the oldest one only when both are taken.
                var rings = equippedItems.Where(i => ((ItemFlags)i.Flags & ItemFlags.ItemRing) != 0).ToList();
                return rings.Count >= 2 ? rings.Take(1) : Enumerable.Empty<ItemInstance>();
            }

            return Enumerable.Empty<ItemInstance>();
        }
    }
}
