using Gothic.Core.Models.Vm;

namespace Gothic.Core.Extensions
{
    /// <summary>
    /// Our fight mode is the weapon itself (VmGothicEnums.WeaponState); the scripts ask for categories.
    /// </summary>
    public static class WeaponStateExtension
    {
        public static bool IsMelee(this VmGothicEnums.WeaponState weaponState)
        {
            return weaponState is VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H;
        }

        public static bool IsRanged(this VmGothicEnums.WeaponState weaponState)
        {
            return weaponState is VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow;
        }

        /// <summary>
        /// A weapon in the hands - melee, ranged or magic, not fists (Npc_HasReadiedWeapon).
        /// </summary>
        public static bool IsWeaponReadied(this VmGothicEnums.WeaponState weaponState)
        {
            return weaponState.IsMelee() || weaponState.IsRanged() || weaponState == VmGothicEnums.WeaponState.Mage;
        }

        /// <summary>
        /// The scripts' category (FMODE_MELEE = 1h or 2h, FMODE_FAR = bow or crossbow) - OpenGothic npc_isinfightmode.
        /// </summary>
        public static VmGothicEnums.FightMode ToFightMode(this VmGothicEnums.WeaponState weaponState)
        {
            if (weaponState == VmGothicEnums.WeaponState.Fist)
                return VmGothicEnums.FightMode.Fists;
            if (weaponState.IsMelee())
                return VmGothicEnums.FightMode.Melee;
            if (weaponState.IsRanged())
                return VmGothicEnums.FightMode.Far;
            return weaponState == VmGothicEnums.WeaponState.Mage
                ? VmGothicEnums.FightMode.Magic
                : VmGothicEnums.FightMode.None;
        }
    }
}
