#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.VR.Services
{
    /// <summary>
    /// VRBow and VRCrossbow's access to arrows/bolts. The logic (ammo, flying projectiles, what happens when they land)
    /// is shared with NPC archers in ProjectileService.
    /// </summary>
    public class VRRangedService
    {
        [Inject] private readonly ProjectileService _projectileService;

        public const float ProjectileSpeed = ProjectileService.ProjectileSpeed;

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
#endif
