using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Arrows and bolts of the hero (VR bow/crossbow, via VRRangedService) and of NPCs: ammo of the weapon, flying
    /// projectiles and what happens when they land. Like Gothic: a missed arrow/bolt lies in the world as a normal item
    /// to pick up again, one that hits an NPC is used up.
    /// </summary>
    public class ProjectileService
    {
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly TextureService _textureService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

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

            var projectile = projectileGo.AddComponent<Projectile>();
            projectile.Owner = shooter;
            projectile.Velocity = direction * speed;
            projectile.UseGravity = true;
            projectile.MaxDistance = _maxDistance;

            projectile.OnNpcHit = (npc, hitPosition, _) =>
            {
                // Stuck in a body that keeps moving - no streak after it.
                StopTrail(projectileGo);
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
            AddTrail(projectileGo);
        }

        // DeveloperConfig.EnableProjectileTrail: the weapon swing's trail look on a flying arrow/bolt.
        private const float _trailSeconds = 0.25f;
        private const float _trailWidth = 0.05f;

        /// <summary>
        /// DeveloperConfig.EnableProjectileTrail: a streak behind the flying arrow/bolt - the weapon trail texture
        /// (ZWEAPONTRAIL.TGA, WeaponTrailOpacity) like the swing of a melee weapon. Not in the original game.
        /// </summary>
        private static void StopTrail(GameObject projectileGo)
        {
            if (!projectileGo.TryGetComponent<TrailRenderer>(out var trail))
                return;
            trail.emitting = false;
            trail.Clear();
        }

        private void AddTrail(GameObject projectileGo)
        {
            if (!_configService.Dev.EnableProjectileTrail || _textureService.WeaponTrailMaterial == null)
                return;

            var trail = projectileGo.AddComponent<TrailRenderer>();
            trail.sharedMaterial = _textureService.WeaponTrailMaterial;
            trail.time = _trailSeconds;
            trail.startWidth = _trailWidth;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.1f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
        }

        // OpenGothic constants.h: hit chance falls from 100% at the shooter to the NPC's chance at the reference
        // range, then to 0 at the max range (DamageCalculator::rangeDamageValue).
        private const float _referenceRangeG1 = 20f;
        private const float _referenceRangeG2 = 15f;
        private const float _maxRange = 45f;
        // A miss passes the target this far from its center (m) - visibly, not through the body.
        private const float _missMinOffset = 0.7f;
        private const float _missMaxOffset = 1.6f;
        private const float _targetHeightOffset = 0.4f;
        private const float _heroChestBelowEyes = 0.35f;

        /// <summary>
        /// DeveloperConfig.EnableNpcPhysicalProjectiles: an NPC's arrow/bolt, like the engine (OpenGothic
        /// Npc::shootBow / World::shootBullet) - from the drawing hand, aimed ballistically at the target's body
        /// (dir.y += g/2 * t), flying with gravity. The hit chance (G1 dexterity, G2 bow/crossbow talent, by distance like
        /// OpenGothic) decides at the shot: a miss is aimed past the target and flies on, the hero can dodge or take
        /// cover. OpenGothic rolls it on impact - an arrow sticking in you without damage looked broken in VR.
        /// Returns false without ammo or ranged weapon (the caller falls back to the old instant hit).
        /// </summary>
        public bool ShootAtTarget(NpcContainer shooter, NpcContainer target)
        {
            if (shooter?.Go == null || target?.Go == null)
                return false;

            var weapon = _npcHelperService.ExtNpcGetEquippedRangedWeapon(shooter.Instance);
            var munition = GetMunition(weapon);
            if (munition == null)
                return false;
            if (!HasAmmo(shooter, munition))
            {
                Logger.Log($"[Projectile] {shooter.Instance.GetName(NpcNameSlot.Slot0)} has no {munition.Name}",
                    LogCat.Fight);
                return false;
            }
            ConsumeAmmo(shooter, munition);

            var origin = GetShotOrigin(shooter);
            var targetPoint = GetTargetPoint(target);
            var distance = Vector3.Distance(origin, targetPoint);
            var hitChance = GetHitChance(shooter, weapon, distance);
            var isHit = Random.value < hitChance;
            if (!isHit)
                targetPoint += GetMissOffset(origin, targetPoint);

            var velocity = GetBallisticVelocity(origin, targetPoint, ProjectileSpeed);
            Logger.Log($"[Projectile] {shooter.Instance.GetName(NpcNameSlot.Slot0)} shoots {munition.Name} at " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)}: {distance:F1} m, chance {hitChance:P0} -> " +
                       $"{(isHit ? "on target" : "miss")}", LogCat.Fight);
            Shoot(shooter, weapon, munition, origin, velocity.normalized, velocity.magnitude, 1f);
            return true;
        }

        /// <summary>
        /// DeveloperConfig.EnableNpcPhysicalProjectiles: nothing solid between the shooter's hand and the target's body.
        /// An archer behind whom the hero hid (a lantern post) shot into the post again and again.
        /// </summary>
        public bool HasLineOfShot(NpcContainer shooter, NpcContainer target)
        {
            if (shooter?.Go == null || target?.Go == null)
                return false;
            return IsShotPathClear(GetShotOrigin(shooter), GetTargetPoint(target));
        }

        // Path segments of the shot's arc that are checked, and the side step tested for a free line (m).
        private const int _shotPathSegments = 8;
        private const float _strafeTestDistance = 1.5f;

        /// <summary>
        /// The real flight: the ballistic arc the shot would take, as a sphere of the projectile's radius - a line to the
        /// target's chest said "free" while the arrow still hit the lantern post next to the hero.
        /// </summary>
        private static bool IsShotPathClear(Vector3 origin, Vector3 targetPoint)
        {
            var velocity = GetBallisticVelocity(origin, targetPoint, ProjectileSpeed);
            var horizontal = new Vector3(targetPoint.x - origin.x, 0f, targetPoint.z - origin.z).magnitude;
            var flightTime = Mathf.Max(horizontal / ProjectileSpeed, 0.01f);
            const float radius = 0.05f;
            const int mask = 1 << 0; // Default layer: world and solid VOBs

            var previous = origin;
            for (var i = 1; i <= _shotPathSegments; i++)
            {
                var t = flightTime * i / _shotPathSegments;
                var point = origin + velocity * t + 0.5f * Physics.gravity * (t * t);
                var step = point - previous;
                var length = step.magnitude;
                if (length > 0.001f &&
                    Physics.SphereCast(previous, radius, step / length, out _, length, mask,
                        QueryTriggerInteraction.Ignore))
                    return false;
                previous = point;
            }
            return true;
        }

        /// <summary>
        /// The side (-1 left, 1 right) from which a step aside gives a free shot, 0 if neither does.
        /// </summary>
        public int FindClearStrafeSide(NpcContainer shooter, NpcContainer target)
        {
            if (shooter?.Go == null || target?.Go == null)
                return 0;

            var origin = GetShotOrigin(shooter);
            var targetPoint = GetTargetPoint(target);
            var right = shooter.Go.transform.right;
            var firstSide = Random.value < 0.5f ? -1 : 1;
            foreach (var side in new[] { firstSide, -firstSide })
            {
                var stepOrigin = origin + right * (side * _strafeTestDistance);
                // The step itself must be possible (no wall right next to him).
                if (Physics.Linecast(origin, stepOrigin, 1 << 0, QueryTriggerInteraction.Ignore))
                    continue;
                if (IsShotPathClear(stepOrigin, targetPoint))
                    return side;
            }
            return 0;
        }

        /// <summary>
        /// A ranged weapon in the inventory with its munition (equipped one first) - like the engine's
        /// Npc_HasRangedWeaponWithAmmo.
        /// </summary>
        public bool HasRangedWeaponWithAmmo(NpcContainer npc)
        {
            if (npc == null)
                return false;
            var equipped = _npcHelperService.ExtNpcGetEquippedRangedWeapon(npc.Instance);
            if (equipped != null && HasAmmo(npc, GetMunition(equipped)))
                return true;

            try
            {
                foreach (var contentItem in _npcInventoryService.GetInventoryItems(npc.Instance,
                             VmGothicEnums.InvCats.InvWeapon))
                {
                    var item = _vmCacheService.TryGetItemData(contentItem.Name);
                    if (item != null && item.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatFf &&
                        HasAmmo(npc, GetMunition(item)))
                        return true;
                }
            }
            catch
            {
                // Weapon category never initialized for this NPC - no weapons.
            }
            return false;
        }

        private static Vector3 GetShotOrigin(NpcContainer shooter)
        {
            var hand = shooter.Go.FindChildRecursively("ZS_RIGHTHAND") ??
                       shooter.Go.FindChildRecursively("BIP01 R HAND");
            var forward = shooter.Go.transform.forward;
            var position = hand != null
                ? hand.transform.position
                : shooter.Go.transform.position + Vector3.up * _targetHeightOffset;
            // Out of the shooter's own body.
            return position + forward * 0.3f;
        }

        private Vector3 GetTargetPoint(NpcContainer target)
        {
            if (target.PrefabProps != null && target.PrefabProps.IsHero())
            {
                var cam = Camera.main;
                if (cam != null)
                    return cam.transform.position - Vector3.up * _heroChestBelowEyes;
            }

            return target.Go.transform.position + Vector3.up * _targetHeightOffset;
        }

        /// <summary>
        /// Like OpenGothic: G1 dexterity %, G2 the NPC's hit chance in the bow/crossbow talent; full at the shooter, that
        /// value at the reference range, 0 at the max range.
        /// </summary>
        private float GetHitChance(NpcContainer shooter, ItemInstance weapon, float distance)
        {
            float chance;
            if (_contextGameVersionService.IsGothic2())
            {
                var isCrossbow = weapon != null &&
                                 ((VmGothicEnums.ItemFlags)weapon.Flags).HasFlag(VmGothicEnums.ItemFlags.ItemCrossbow);
                var talentName = isCrossbow ? "NPC_TALENT_CROSSBOW" : "NPC_TALENT_BOW";
                var talent = _gameStateService.GothicVm.GetSymbolByName(talentName)?.GetInt(0) ?? (isCrossbow ? 4 : 3);
                chance = shooter.Vob.GetHitChance(talent) / 100f;
            }
            else
            {
                chance = shooter.Vob.GetAttribute((int)NpcAttribute.Dexterity) / 100f;
            }
            chance = Mathf.Clamp01(chance);

            var referenceRange = _contextGameVersionService.IsGothic2() ? _referenceRangeG2 : _referenceRangeG1;
            if (distance < referenceRange)
                return Mathf.Lerp(1f, chance, distance / referenceRange);
            if (distance < _maxRange)
                return Mathf.Lerp(chance, 0f, (distance - referenceRange) / (_maxRange - referenceRange));
            return 0f;
        }

        /// <summary>
        /// Sideways (and a bit up/down) past the target, perpendicular to the line of fire.
        /// </summary>
        private static Vector3 GetMissOffset(Vector3 origin, Vector3 targetPoint)
        {
            var line = (targetPoint - origin).normalized;
            var side = Vector3.Cross(Vector3.up, line).normalized;
            if (side.sqrMagnitude < 0.01f)
                side = Vector3.right;
            var direction = (side * (Random.value < 0.5f ? -1f : 1f) + Vector3.up * Random.Range(-0.4f, 0.4f))
                .normalized;
            return direction * Random.Range(_missMinOffset, _missMaxOffset);
        }

        /// <summary>
        /// OpenGothic World::shootBullet: horizontal speed fixed, the time to the target lifts the shot by g/2 * t.
        /// </summary>
        private static Vector3 GetBallisticVelocity(Vector3 origin, Vector3 target, float speed)
        {
            var delta = target - origin;
            var horizontal = new Vector3(delta.x, 0f, delta.z).magnitude;
            var time = Mathf.Max(horizontal / speed, 0.01f);
            var velocity = delta / time;
            velocity.y += 0.5f * -Physics.gravity.y * time;
            return velocity;
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
