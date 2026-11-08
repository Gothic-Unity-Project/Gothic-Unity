using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.VR.Services
{
    /// <summary>
    /// The hero's ranged weapons in VR: which held item is a bow or a crossbow (VRVobItem, VRWeaponService), who may
    /// shoot a crossbow with one hand, and VRBow and VRCrossbow's access to arrows/bolts. The projectile logic (ammo,
    /// flying projectiles, what happens when they land) is shared with NPC archers in ProjectileService.
    /// </summary>
    public class VRRangedService
    {
        [Inject] private readonly ProjectileService _projectileService;
        [Inject] private readonly NpcHelperService _npcHelperService;

        private const int _masterSkill = 2;
        private const int _oneHandPercent = 60;

        public const float ProjectileSpeed = ProjectileService.ProjectileSpeed;

        /// <summary>
        /// Bow (ITEM_KAT_FF without ITEM_CROSSBOW), CBow (ITEM_CROSSBOW) or NoWeapon (no ranged weapon). The one
        /// place that tells which ranged component (VRBow/VRCrossbow) an item gets and how the hero counts as armed.
        /// </summary>
        public VmGothicEnums.WeaponState GetRangedState(ItemInstance item)
        {
            if (!item.IsRangedWeapon())
                return VmGothicEnums.WeaponState.NoWeapon;
            return item.IsCrossbow() ? VmGothicEnums.WeaponState.CBow : VmGothicEnums.WeaponState.Bow;
        }

        /// <summary>
        /// Only a trained crossbowman shoots with one hand (sword in the other).
        /// G1/mods: crossbow talent skill "master" or 60 %+; G2: crossbow hitchance 60 %+.
        /// </summary>
        public bool IsOneHandedCrossbow(NpcContainer hero)
        {
            if (hero == null)
                return true;

            var talentIndex = (int)NpcTalent.Crossbow;
            var talent = hero.Vob.GetTalent(talentIndex);
            var percent = _npcHelperService.GetTalentPercent(hero, talentIndex);
            return (talent?.Skill ?? 0) >= _masterSkill || percent >= _oneHandPercent;
        }

        public ItemInstance GetMunition(ItemInstance weapon) => _projectileService.GetMunition(weapon);

        public bool HasAmmo(NpcContainer shooter, ItemInstance munition) =>
            _projectileService.HasAmmo(shooter, munition);

        public void ConsumeAmmo(NpcContainer shooter, ItemInstance munition) =>
            _projectileService.ConsumeAmmo(shooter, munition);

        public GameObject CreateAmmoVisual(ItemInstance munition, Transform parent) =>
            _projectileService.CreateAmmoVisual(munition, parent);

        /// <summary>
        /// Shoots an arrow/bolt. The visual (from CreateAmmoVisual) flies with it, or a new one is created.
        /// damageScale: VR bows hit weaker when only partly drawn.
        /// </summary>
        public void Shoot(NpcContainer shooter, ItemInstance weapon, ItemInstance munition, Vector3 position,
            Vector3 direction, float speed, float damageScale, GameObject visual = null) =>
            _projectileService.Shoot(shooter, weapon, munition, position, direction, speed, damageScale, visual);

        public static Vector3 GetLongAxis(GameObject root, bool towardsWiderEnd, bool isAsymmetryAxis = false) =>
            ProjectileService.GetLongAxis(root, towardsWiderEnd, isAsymmetryAxis);

        public static Bounds GetLocalBounds(GameObject root) => ProjectileService.GetLocalBounds(root);
    }
}
