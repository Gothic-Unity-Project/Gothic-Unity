using System.Collections.Generic;
using GUZ.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Debugging
{
    /// <summary>
    /// Visualizes all NPC bone BoxColliders as transparent cube meshes.
    /// Works in all builds including release.
    ///
    /// Color coding:
    ///   Grey  (30% alpha) = collider inactive
    ///   Green (30% alpha) = collider enabled (e.g. attack window active)
    ///   Red   (60% alpha) = another collider is currently overlapping this one
    ///
    /// Toggle via DeveloperConfig.ShowNpcColliders.
    /// </summary>
    public class NpcColliderDebugAdapter : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;

        private BoxCollider[] _colliders;
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

            // Bone colliders are added by NpcMeshBuilder after Start() runs.
            _colliders = GetComponentsInChildren<BoxCollider>(includeInactive: true);

            if (_colliders.Length > 0)
                CreateDebugObjects();
        }

        private void CreateDebugObjects()
        {
            var cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var baseMaterial = Resources.Load<Material>("Materials/NpcColliderDebug");

            foreach (var col in _colliders)
            {
                var debugGo = new GameObject("_ColliderDebug");
                debugGo.transform.SetParent(col.transform, false);
                debugGo.transform.localPosition = col.center;
                debugGo.transform.localScale = col.size;

                var filter = debugGo.AddComponent<MeshFilter>();
                filter.mesh = cubeMesh;

                // Use renderer.material (not sharedMaterial) to get a unique instance per
                // collider so we can set individual colors without MaterialPropertyBlock.
                var meshRenderer = debugGo.AddComponent<MeshRenderer>();
                meshRenderer.material = baseMaterial;

                _debugObjects.Add(debugGo);
                _debugRenderers.Add(meshRenderer);

                // Track trigger events on the bone GO itself to detect hits.
                var tracker = col.gameObject.GetComponent<ColliderHitTracker>()
                              ?? col.gameObject.AddComponent<ColliderHitTracker>();
                _hitTrackers.Add(tracker);
            }
        }

        private void UpdateColors()
        {
            for (var i = 0; i < _debugRenderers.Count; i++)
            {
                Color color;
                if (_hitTrackers[i].IsBeingHit)
                    color = _colorHit;
                else if (_colliders[i].enabled)
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
