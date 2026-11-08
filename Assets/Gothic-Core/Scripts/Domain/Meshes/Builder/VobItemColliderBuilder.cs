using System.Collections.Generic;
using Gothic.Core.Domain.StaticCache;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.StaticCache;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Meshes.Builder
{
    /// <summary>
    /// Adds an item's colliders. It only chooses where they come from, the geometry lives in the domains:
    /// 1. The pre-cached colliders (VobItemColliderCacheCreatorDomain) - bows and crossbows are fitted there too.
    /// 2. Without pre-cached colliders (other morph mesh items, an old cache): calculated from the item's mesh -
    ///    bows and crossbows fitted (VobItemFittedColliderCacheCreatorDomain.CreateRangedWeapon), everything else
    ///    boxes sliced along the mesh (ItemColliderSlicingDomain), a thin capsule if it can't be sliced.
    /// Colliders calculated from a mesh are cached per mesh name (MultiTypeCacheService.ItemColliders). Oriented boxes
    /// become convex box meshes on the item itself: child objects with BoxColliders would keep their layer when the
    /// grabbed item switches to VobItemNoWorldCollision. The box meshes are shared like the item meshes
    /// (MultiTypeCacheService.Meshes).
    /// </summary>
    public class VobItemColliderBuilder
    {
        [Inject] private readonly StaticCacheService _staticCacheService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        private static readonly int[] _boxTriangles =
        {
            0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4,
            2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5
        };

        private const float _fallbackCapsuleRadius = 0.025f;
        private const float _fallbackCapsuleMinHeight = 0.1f;


        /// <summary>
        /// kind: from the item's flags (bow, crossbow or anything else). mesh: the item's rendered mesh - items without
        /// pre-cached colliders get them from it.
        /// </summary>
        public void AddColliders(GameObject rootGo, string meshName, ItemColliderKind kind, Mesh mesh = null)
        {
            if (_staticCacheService.LoadedVobItemColliders.TryGetValue(meshName, out var colliders) &&
                colliders.Count > 0)
            {
                AddColliders(rootGo, $"{meshName}_Collider", colliders);
                return;
            }

            if (mesh != null)
            {
                AddMeshColliders(rootGo, meshName, kind, mesh);
                return;
            }

            Logger.LogError($"Can't find Collider data for {meshName}. Skipping...", LogCat.Mesh);
        }

        /// <summary>
        /// boxMeshKeyPrefix: oriented boxes are cached as "{prefix}{index}" meshes.
        /// </summary>
        private void AddColliders(GameObject rootGo, string boxMeshKeyPrefix,
            List<VobItemColliderCacheCreatorDomain.Data> colliders)
        {
            for (var i = 0; i < colliders.Count; i++)
            {
                var coll = colliders[i];
                switch (coll.T)
                {
                    case VobItemColliderCacheCreatorDomain.T.B:
                        var boxCollider = rootGo.AddComponent<BoxCollider>();
                        boxCollider.center = coll.C;
                        boxCollider.size = coll.S;
                        break;
                    case VobItemColliderCacheCreatorDomain.T.C:
                        var capsuleCollider = rootGo.AddComponent<CapsuleCollider>();
                        capsuleCollider.center = coll.C;
                        capsuleCollider.direction = coll.D;
                        capsuleCollider.height = coll.H;
                        capsuleCollider.radius = coll.R;
                        break;
                    case VobItemColliderCacheCreatorDomain.T.O:
                        var meshCollider = rootGo.AddComponent<MeshCollider>();
                        meshCollider.convex = true;
                        meshCollider.sharedMesh = GetBoxMesh($"{boxMeshKeyPrefix}{i}", coll.C, coll.S, coll.Q);
                        break;
                    default:
                        Logger.LogError($"Collider cache type {coll.T} not yet handled", LogCat.Mesh);
                        break;
                }
            }
        }

        private Mesh GetBoxMesh(string cacheKey, Vector3 center, Vector3 size, Quaternion rotation)
        {
            if (_multiTypeCacheService.Meshes.TryGetValue(cacheKey, out var boxMesh))
                return boxMesh;

            var half = size * 0.5f;
            var corners = new List<Vector3>(8);
            for (var i = 0; i < 8; i++)
            {
                var local = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);
                corners.Add(center + rotation * local);
            }

            boxMesh = new Mesh { name = cacheKey };
            boxMesh.SetVertices(corners);
            boxMesh.SetTriangles(_boxTriangles, 0);
            boxMesh.RecalculateBounds();

            _multiTypeCacheService.Meshes.Add(cacheKey, boxMesh);
            return boxMesh;
        }

        /// <summary>
        /// Colliders from the item's own mesh, calculated once per mesh. Nothing to fit or slice: a thin capsule along
        /// the mesh.
        /// </summary>
        private void AddMeshColliders(GameObject rootGo, string meshName, ItemColliderKind kind, Mesh mesh)
        {
            var colliders = GetMeshColliders(meshName, kind, mesh);
            if (colliders.Count > 0)
            {
                AddColliders(rootGo, $"{meshName}_MeshCollider", colliders);
                return;
            }

            var bounds = mesh.bounds;
            var axis = ItemColliderSlicingDomain.GetLongestAxis(bounds.size);
            var capsule = rootGo.AddComponent<CapsuleCollider>();
            capsule.center = bounds.center;
            capsule.direction = axis;
            capsule.height = Mathf.Max(bounds.size[axis], _fallbackCapsuleMinHeight);
            capsule.radius = _fallbackCapsuleRadius;
        }

        private List<VobItemColliderCacheCreatorDomain.Data> GetMeshColliders(string meshName, ItemColliderKind kind,
            Mesh mesh)
        {
            if (_multiTypeCacheService.ItemColliders.TryGetValue(meshName, out var colliders))
                return colliders;

            // Not cached: the mesh might be readable on the next spawn.
            if (!mesh.isReadable)
                return new List<VobItemColliderCacheCreatorDomain.Data>();

            colliders = CalculateMeshColliders(meshName, kind, mesh);
            _multiTypeCacheService.ItemColliders.Add(meshName, colliders);
            return colliders;
        }

        /// <summary>
        /// Bows and crossbows are normally fitted while pre-caching. Here only if the cache has no entry for them.
        /// </summary>
        private static List<VobItemColliderCacheCreatorDomain.Data> CalculateMeshColliders(string meshName,
            ItemColliderKind kind, Mesh mesh)
        {
            Logger.LogWarning($"No cached colliders for {meshName} - calculated from its mesh.", LogCat.Mesh);

            if (kind == ItemColliderKind.Default)
                return VobItemFittedColliderCacheCreatorDomain.CreateSliced(mesh.vertices, false);

            return VobItemFittedColliderCacheCreatorDomain.CreateRangedWeapon(meshName, mesh.vertices,
                mesh.triangles, kind == ItemColliderKind.Bow);
        }
    }
}
