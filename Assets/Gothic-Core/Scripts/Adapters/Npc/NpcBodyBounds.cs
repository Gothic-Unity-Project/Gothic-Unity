using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Config;
using Gothic.VR.Adapters.Npc;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Npc
{
    /// <summary>
    /// The NPC's body as a box in its root's space (yaw only), following the animation: the bones' current positions
    /// plus the skin around them. Everything else took the root bone BIP01 - for a shadowbeast that's at its hind legs,
    /// the head is 2 m in front: the hitbox (on the rotated BIP01, sideways) missed the whole front, the personal space
    /// let the head into the camera, and an orc leaning into his swing came through it too.
    /// Only updated near the camera (fights, talking); further away the last box stays.
    /// </summary>
    public class NpcBodyBounds : MonoBehaviour
    {
        private const float _updateInterval = 0.1f;
        private const float _updateDistance = 12f;
        private const float _minPadding = 0.05f;
        private const float _maxPadding = 1f;
        private const float _hitboxPadding = 0.1f;

        // Arms (and a quadruped's forelegs) swing far out in attacks - they count for being hit, not as the solid body.
        private static readonly string[] _limbBoneTags = { "CLAVICLE", "UPPERARM", "FOREARM", "HAND", "FINGER" };

        private ConfigService _configService;
        private Transform[] _bones;
        private bool[] _isLimb;
        private Vector3 _paddingMin;
        private Vector3 _paddingMax;
        private bool _isCalibrated;
        private float _nextUpdateTime;
        private BoxCollider _hitbox;

        /// <summary>
        /// The whole body (with arms) in the NPC root's space.
        /// </summary>
        public Bounds Body { get; private set; }

        /// <summary>
        /// The body without arms/forelegs in the NPC root's space - what nothing may walk into.
        /// </summary>
        public Bounds Core { get; private set; }

        public bool IsReady => _isCalibrated;

        /// <summary>
        /// The NPC's body bounds, added on first use. Null if the NPC has no GameObject.
        /// </summary>
        public static NpcBodyBounds Get(NpcContainer npc)
        {
            if (npc?.Go == null)
                return null;
            return npc.Go.TryGetComponent<NpcBodyBounds>(out var bodyBounds)
                ? bodyBounds
                : npc.Go.AddComponent<NpcBodyBounds>();
        }

        private void Awake()
        {
            _configService = ReflexProjectInstaller.DIContainer.Resolve<ConfigService>();
        }

        private void LateUpdate()
        {
            if (Time.time < _nextUpdateTime)
                return;
            _nextUpdateTime = Time.time + _updateInterval;

            var cam = Camera.main;
            if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > _updateDistance * _updateDistance)
                return;

            if (_bones == null)
                CollectBones();
            if (_bones.Length == 0)
                return;

            if (!_isCalibrated && !TryCalibrate())
                return;

            UpdateBounds();
            UpdateHitbox();
        }

        private void CollectBones()
        {
            var bones = new List<Transform>();
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (child != transform && child.name.StartsWith("BIP01"))
                    bones.Add(child);
            }

            _bones = bones.ToArray();
            _isLimb = new bool[_bones.Length];
            for (var i = 0; i < _bones.Length; i++)
            {
                var boneName = _bones[i].name.ToUpperInvariant();
                foreach (var tag in _limbBoneTags)
                {
                    if (boneName.Contains(tag))
                        _isLimb[i] = true;
                }
            }
        }

        /// <summary>
        /// The skin around the bones, per side: the skinned mesh in its current pose against the bones' box.
        /// </summary>
        private bool TryCalibrate()
        {
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers.Length == 0)
                return false;

            var meshMin = Vector3.positiveInfinity;
            var meshMax = Vector3.negativeInfinity;
            var bakedMesh = new Mesh();
            foreach (var skinnedRenderer in renderers)
            {
                skinnedRenderer.BakeMesh(bakedMesh, true);
                var rendererTransform = skinnedRenderer.transform;
                foreach (var vertex in bakedMesh.vertices)
                {
                    var local = transform.InverseTransformPoint(rendererTransform.TransformPoint(vertex));
                    meshMin = Vector3.Min(meshMin, local);
                    meshMax = Vector3.Max(meshMax, local);
                }
            }
            Destroy(bakedMesh);

            if (float.IsInfinity(meshMin.x))
                return false;

            var bones = GetBoneBounds(false);
            _paddingMin = Clamp(bones.min - meshMin);
            _paddingMax = Clamp(meshMax - bones.max);
            _isCalibrated = true;

            UpdateBounds();
            Logger.Log($"[BodyBounds] {transform.parent?.name}: body {Body.min:F2}..{Body.max:F2}, skin " +
                       $"{_paddingMin:F2}/{_paddingMax:F2}", LogCat.Npc);
            return true;
        }

        private static Vector3 Clamp(Vector3 padding)
        {
            return new Vector3(Mathf.Clamp(padding.x, _minPadding, _maxPadding),
                Mathf.Clamp(padding.y, _minPadding, _maxPadding), Mathf.Clamp(padding.z, _minPadding, _maxPadding));
        }

        private Bounds GetBoneBounds(bool isCoreOnly)
        {
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            for (var i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null || (isCoreOnly && _isLimb[i]))
                    continue;
                var local = transform.InverseTransformPoint(_bones[i].position);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }

            var bounds = new Bounds();
            if (!float.IsInfinity(min.x))
                bounds.SetMinMax(min, max);
            return bounds;
        }

        private void UpdateBounds()
        {
            var body = GetBoneBounds(false);
            body.SetMinMax(body.min - _paddingMin, body.max + _paddingMax);
            Body = body;

            var core = GetBoneBounds(true);
            core.SetMinMax(core.min - _paddingMin, core.max + _paddingMax);
            Core = core;
        }

        /// <summary>
        /// DeveloperConfig.EnableAnimatedNpcHitbox: the weapon hitbox is the body box (+ padding for imprecise VR swings),
        /// under the NPC root instead of the rotated BIP01.
        /// </summary>
        private void UpdateHitbox()
        {
            if (!_configService.Dev.EnableAnimatedNpcHitbox)
                return;

            if (_hitbox == null)
            {
                var adapter = GetComponentInChildren<NpcHitboxColliderAdapter>(true);
                if (adapter == null || !adapter.TryGetComponent(out _hitbox))
                    return;
                adapter.transform.SetParent(transform, false);
                adapter.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                adapter.transform.localScale = Vector3.one;
            }

            _hitbox.center = Body.center;
            _hitbox.size = Body.size + Vector3.one * (_hitboxPadding * 2f);
        }

        /// <summary>
        /// How far (m) the root has to move horizontally, and where to, so a world position is margin away from the solid
        /// body. Zero if it already is.
        /// </summary>
        public Vector3 GetPushOut(Vector3 worldPosition, float margin)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            var core = Core;
            var dx = local.x < core.min.x ? core.min.x - local.x : local.x > core.max.x ? local.x - core.max.x : 0f;
            var dz = local.z < core.min.z ? core.min.z - local.z : local.z > core.max.z ? local.z - core.max.z : 0f;

            Vector3 localPush;
            if (dx > 0f || dz > 0f)
            {
                // Outside: move the body straight away from the position until the gap is margin.
                var distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance >= margin)
                    return Vector3.zero;
                var closest = new Vector3(Mathf.Clamp(local.x, core.min.x, core.max.x), 0f,
                    Mathf.Clamp(local.z, core.min.z, core.max.z));
                var away = closest - new Vector3(local.x, 0f, local.z);
                localPush = away.normalized * (margin - distance);
            }
            else
            {
                // Inside: out through the nearest side.
                var toMinX = local.x - core.min.x;
                var toMaxX = core.max.x - local.x;
                var toMinZ = local.z - core.min.z;
                var toMaxZ = core.max.z - local.z;
                var nearest = Mathf.Min(Mathf.Min(toMinX, toMaxX), Mathf.Min(toMinZ, toMaxZ));
                if (nearest == toMinX)
                    localPush = new Vector3(toMinX + margin, 0f, 0f);
                else if (nearest == toMaxX)
                    localPush = new Vector3(-(toMaxX + margin), 0f, 0f);
                else if (nearest == toMinZ)
                    localPush = new Vector3(0f, 0f, toMinZ + margin);
                else
                    localPush = new Vector3(0f, 0f, -(toMaxZ + margin));
            }

            var worldPush = transform.TransformVector(localPush);
            worldPush.y = 0f;
            return worldPush;
        }
    }
}
