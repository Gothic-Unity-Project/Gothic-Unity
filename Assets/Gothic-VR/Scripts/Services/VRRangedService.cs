#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.Vob.VobItem;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Shared by VRBow and VRCrossbow (vr-ranged-spells-plan.md): ammo of the weapon, flying arrows/bolts and what
    /// happens when they land. Like Gothic: a missed arrow/bolt lies in the world as a normal item to pick up again,
    /// one that hits an NPC is used up.
    /// </summary>
    public class VRRangedService
    {
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly GameStateService _gameStateService;

        // OpenGothic DynamicWorld::bulletSpeed (3 cm/ms).
        public const float ProjectileSpeed = 30f;
        private const float _stuckInNpcSeconds = 20f;
        private const float _maxDistance = 120f;
        // G1 / G2 names - only used when a weapon has no munition set.
        private static readonly string[] _fallbackArrows = { "ItAmArrow", "ItRw_Arrow" };
        private static readonly string[] _fallbackBolts = { "ItAmBolt", "ItRw_Bolt" };


        /// <summary>
        /// The weapon's munition item (C_Item.munition). Bows shoot arrows, crossbows bolts - fallback by weapon flag.
        /// </summary>
        public ItemInstance GetMunition(ItemInstance weapon)
        {
            if (weapon == null)
                return null;

            var munition = weapon.Munition > 0 ? _vmCacheService.TryGetItemData(weapon.Munition) : null;
            if (munition != null)
                return munition;

            var isCrossbow = ((VmGothicEnums.ItemFlags)weapon.Flags).HasFlag(VmGothicEnums.ItemFlags.ItemCrossbow);
            foreach (var name in isCrossbow ? _fallbackBolts : _fallbackArrows)
            {
                if (_gameStateService.GothicVm.GetSymbolByName(name) != null)
                    return _vmCacheService.TryGetItemData(name);
            }
            return null;
        }

        public bool HasAmmo(NpcContainer shooter, ItemInstance munition)
        {
            return shooter != null && munition != null && _npcInventoryService.ExtNpcHasItems(shooter.Instance, munition.Index) > 0;
        }

        public void ConsumeAmmo(NpcContainer shooter, ItemInstance munition)
        {
            _npcInventoryService.ExtRemoveInvItems(shooter.Instance, munition.Index, 1);
        }

        /// <summary>
        /// Mesh-only copy of the munition item (no colliders), its tip turned to the parent's +Z.
        /// </summary>
        public GameObject CreateAmmoVisual(ItemInstance munition, Transform parent)
        {
            var visualRoot = new GameObject($"Ammo ({munition.Name})");
            visualRoot.transform.SetParent(parent, false);

            var meshRoot = new GameObject("Mesh");
            meshRoot.transform.SetParent(visualRoot.transform, false);
            var visual = _vobService.CreateItemMesh(munition.Index, meshRoot);
            if (visual == null)
                return visualRoot;

            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var meshCollider in meshRoot.GetComponentsInChildren<Collider>(true))
                Object.Destroy(meshCollider);

            var tipAxis = GetLongAxis(meshRoot, towardsWiderEnd: false);
            meshRoot.transform.localRotation = Quaternion.FromToRotation(tipAxis, Vector3.forward);
            return visualRoot;
        }

        /// <summary>
        /// Shoots an arrow/bolt. The visual (from CreateAmmoVisual) flies with it, or a new one is created.
        /// damageScale: VR bows hit weaker when only partly drawn.
        /// </summary>
        public void Shoot(NpcContainer shooter, ItemInstance weapon, ItemInstance munition, Vector3 position,
            Vector3 direction, float speed, float damageScale, GameObject visual = null)
        {
            var projectileGo = new GameObject($"Projectile ({munition.Name})");
            projectileGo.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));

            if (visual == null)
            {
                visual = CreateAmmoVisual(munition, projectileGo.transform);
            }
            else
            {
                visual.transform.SetParent(projectileGo.transform, true);
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            var projectile = projectileGo.AddComponent<VRProjectile>();
            projectile.Owner = shooter;
            projectile.Velocity = direction * speed;
            projectile.UseGravity = true;
            projectile.MaxDistance = _maxDistance;

            projectile.OnNpcHit = (npc, hitPosition, _) =>
            {
                // Used up - stays stuck in the target for a while (visual only).
                projectileGo.transform.SetParent(npc.Go.transform, true);
                GlobalEventDispatcher.RangedHit.Invoke(shooter, npc, hitPosition, (weapon, damageScale));
                Object.Destroy(projectileGo, _stuckInNpcSeconds);
            };
            projectile.OnWorldHit = (hitPosition, normal) =>
            {
                // Missed: a real item to pick up again (saved with the world like dropped items).
                Object.Destroy(projectileGo);
                _vobService.DropItemAtPosition(munition.Index, hitPosition + normal * 0.1f);
                Logger.Log($"[VRRangedService] {munition.Name} missed - lies at {hitPosition}", LogCat.VR);
            };
            projectile.OnExpired = () => Object.Destroy(projectileGo);
        }

        /// <summary>
        /// Long axis of all meshes below root (in root space), its sign pointing to the end whose cross section is
        /// wider (crossbow prod) or narrower (arrow/bolt tip).
        /// With isAsymmetryAxis, the axis among the two longest ones whose halves differ most is taken: a crossbow's
        /// prod can be wider than the crossbow is long, but only the barrel axis has a wide front and a narrow stock.
        /// </summary>
        public static Vector3 GetLongAxis(GameObject root, bool towardsWiderEnd, bool isAsymmetryAxis = false)
        {
            var vertices = GetLocalVertices(root);
            if (vertices.Count == 0)
                return Vector3.forward;

            var bounds = GetBounds(vertices);
            var size = bounds.size;
            var axesBySize = new[] { 0, 1, 2 };
            System.Array.Sort(axesBySize, (a, b) => size[b].CompareTo(size[a]));

            var bestAxis = axesBySize[0];
            GetHalfSpreads(vertices, bounds.center, bestAxis, out var bestPositive, out var bestNegative);
            if (isAsymmetryAxis)
            {
                GetHalfSpreads(vertices, bounds.center, axesBySize[1], out var positive, out var negative);
                if (GetAsymmetry(positive, negative) > GetAsymmetry(bestPositive, bestNegative))
                {
                    bestAxis = axesBySize[1];
                    bestPositive = positive;
                    bestNegative = negative;
                }
            }

            var axis = Vector3.zero;
            axis[bestAxis] = 1f;
            var isPositiveWider = bestPositive >= bestNegative;
            return isPositiveWider == towardsWiderEnd ? axis : -axis;
        }

        public static Bounds GetLocalBounds(GameObject root)
        {
            var vertices = GetLocalVertices(root);
            return vertices.Count == 0 ? new Bounds(Vector3.zero, Vector3.one * 0.1f) : GetBounds(vertices);
        }

        private static Bounds GetBounds(List<Vector3> vertices)
        {
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var vertex in vertices)
                bounds.Encapsulate(vertex);
            return bounds;
        }

        /// <summary>
        /// Largest squared distance from the axis line, separately for the vertices on the positive/negative side.
        /// </summary>
        private static void GetHalfSpreads(List<Vector3> vertices, Vector3 center, int axisIndex,
            out float positiveSpread, out float negativeSpread)
        {
            positiveSpread = 0f;
            negativeSpread = 0f;
            foreach (var vertex in vertices)
            {
                var perpendicular = vertex - center;
                perpendicular[axisIndex] = 0f;
                var spread = perpendicular.sqrMagnitude;
                if (vertex[axisIndex] >= center[axisIndex])
                    positiveSpread = Mathf.Max(positiveSpread, spread);
                else
                    negativeSpread = Mathf.Max(negativeSpread, spread);
            }
        }

        private static float GetAsymmetry(float positiveSpread, float negativeSpread)
        {
            var smaller = Mathf.Min(positiveSpread, negativeSpread);
            var larger = Mathf.Max(positiveSpread, negativeSpread);
            return smaller > 0f ? larger / smaller : float.MaxValue;
        }

        private static List<Vector3> GetLocalVertices(GameObject root)
        {
            var result = new List<Vector3>();
            foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = meshFilter.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                    continue;

                var toRoot = root.transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
                foreach (var vertex in mesh.vertices)
                    result.Add(toRoot.MultiplyPoint3x4(vertex));
            }
            return result;
        }
    }
}
#endif
