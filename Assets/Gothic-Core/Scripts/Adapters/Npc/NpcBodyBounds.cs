using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.VR.Adapters.Npc;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Npc
{
    /// <summary>
    /// The NPC's body as a box in its root's space (yaw only), following the animation: the bones' current positions
    /// plus the skin around them. It shapes the weapon hitbox - on the root bone BIP01 alone (for a shadowbeast at its
    /// hind legs, the head 2 m in front) the rotated, sideways hitbox missed the whole front.
    /// Only updated near the camera (fights, talking); further away the last box stays.
    /// </summary>
    public class NpcBodyBounds : MonoBehaviour
    {
        private const float _updateInterval = 0.1f;
        private const float _updateDistance = 12f;
        private const float _minPadding = 0.05f;
        private const float _maxPadding = 1f;
        private const float _hitboxPadding = 0.1f;

        private Transform[] _bones;
        private Vector3 _paddingMin;
        private Vector3 _paddingMax;
        private bool _isCalibrated;
        private float _nextUpdateTime;
        private BoxCollider _hitbox;

        // The whole body (with arms) in the NPC root's space.
        private Bounds _body;

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

            var bones = GetBoneBounds();
            _paddingMin = Clamp(bones.min - meshMin);
            _paddingMax = Clamp(meshMax - bones.max);
            _isCalibrated = true;

            UpdateBounds();
            Logger.Log($"[BodyBounds] {transform.parent?.name}: body {_body.min:F2}..{_body.max:F2}, skin " +
                       $"{_paddingMin:F2}/{_paddingMax:F2}", LogCat.Npc);
            return true;
        }

        private static Vector3 Clamp(Vector3 padding)
        {
            return new Vector3(Mathf.Clamp(padding.x, _minPadding, _maxPadding),
                Mathf.Clamp(padding.y, _minPadding, _maxPadding), Mathf.Clamp(padding.z, _minPadding, _maxPadding));
        }

        private Bounds GetBoneBounds()
        {
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            for (var i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null)
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
            var body = GetBoneBounds();
            body.SetMinMax(body.min - _paddingMin, body.max + _paddingMax);
            _body = body;
        }

        /// <summary>
        /// The weapon hitbox is the body box (+ padding for imprecise VR swings),
        /// under the NPC root instead of the rotated BIP01.
        /// </summary>
        private void UpdateHitbox()
        {
            if (_hitbox == null)
            {
                var adapter = GetComponentInChildren<NpcHitboxColliderAdapter>(true);
                if (adapter == null || !adapter.TryGetComponent(out _hitbox))
                    return;
                adapter.transform.SetParent(transform, false);
                adapter.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                adapter.transform.localScale = Vector3.one;
            }

            _hitbox.center = _body.center;
            _hitbox.size = _body.size + Vector3.one * (_hitboxPadding * 2f);
        }
    }
}
