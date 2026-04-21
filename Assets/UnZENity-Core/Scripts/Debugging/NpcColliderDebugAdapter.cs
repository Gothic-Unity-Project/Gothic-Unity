using System.Collections.Generic;
using GUZ.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Debugging
{
    /// <summary>
    /// Visualizes NPC BoxCollider as transparent cube mesh.
    /// Shows the single root AABB (Gothic-style) plus any active DEF_HIT_LIMB bone colliders.
    /// Works in all builds including release.
    ///
    /// Color coding:
    ///   Grey  (15% alpha) = collider disabled (combat inactive)
    ///   Green (30% alpha) = collider enabled (combat active / attack window open)
    ///   Red   (60% alpha) = another collider is currently overlapping this one
    ///
    /// Toggle via DeveloperConfig.ShowNpcColliders.
    /// </summary>
    public class NpcColliderDebugAdapter : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;

        private BoxCollider _collider;
        private readonly List<GameObject> _debugObjects = new();
        private readonly List<MeshRenderer> _debugRenderers = new();
        private readonly List<ColliderHitTracker> _hitTrackers = new();

        private static readonly Color _colorInactive = new(1f, 1f, 1f, 0.15f);
        private static readonly Color _colorActive   = new(0f, 1f, 0f, 0.3f);
        private static readonly Color _colorHit      = new(1f, 0f, 0f, 0.6f);

        private void Start()
        {
            if (!_configService.Dev.ShowNpcColliders)
                Destroy(this);
        }

        private void Update()
        {
            // Once debug objects exist, only update colors each frame.
            if (_debugObjects.Count > 0)
            {
                UpdateColors();
                return;
            }

            // Colliders are added by NpcMeshBuilder after Start() runs.
            _collider = GetComponent<BoxCollider>();

            if (_collider != null)
                CreateDebugObject();
        }

        private void CreateDebugObject()
        {
            var cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var baseMaterial = Resources.Load<Material>("Materials/NpcColliderDebug");

            var debugGo = new GameObject("_ColliderDebug");
            debugGo.transform.SetParent(_collider.transform, false);
            debugGo.transform.localPosition = _collider.center;
            debugGo.transform.localScale = _collider.size;

            var filter = debugGo.AddComponent<MeshFilter>();
            filter.mesh = cubeMesh;

            // Use renderer.material (not sharedMaterial) to get a unique instance per
            // collider so we can set individual colors without MaterialPropertyBlock.
            var meshRenderer = debugGo.AddComponent<MeshRenderer>();
            meshRenderer.material = baseMaterial;

            _debugObjects.Add(debugGo);
            _debugRenderers.Add(meshRenderer);

            // Track trigger events on the bone GO itself to detect hits.
            var tracker = _collider.gameObject.GetComponent<ColliderHitTracker>()
                          ?? _collider.gameObject.AddComponent<ColliderHitTracker>();
            _hitTrackers.Add(tracker);
        }

        private void UpdateColors()
        {
            for (var i = 0; i < _debugRenderers.Count; i++)
            {
                Color color;
                if (_hitTrackers[i].IsBeingHit)
                    color = _colorHit;
                else if (_collider.enabled)
                     color = _colorActive;
                else
                    color = _colorInactive;

                _debugRenderers[i].material.color = color;
            }
        }

        private void OnDestroy()
        {
            foreach (var r in _debugRenderers)
            {
                if (r != null)
                    Destroy(r.material);
            }

            foreach (var go in _debugObjects)
            {
                if (go != null)
                    Destroy(go);
            }
        }
    }
}
