using System.Collections.Generic;
using Gothic.Core.Domain.StaticCache;
using Gothic.Core.Logging;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.StaticCache;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Meshes.Builder
{
    /// <summary>
    /// Adds an item's pre-cached colliders (VobItemColliderCacheCreatorDomain). The oriented boxes of melee weapons
    /// become convex box meshes on the item itself: child objects with BoxColliders would keep their layer when the
    /// grabbed item switches to VobItemNoWorldCollision. The box meshes are shared like the item meshes
    /// (MultiTypeCacheService).
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


        public void AddColliders(GameObject rootGo, string meshName)
        {
            if (!_staticCacheService.LoadedVobItemColliders.TryGetValue(meshName, out var colliders))
            {
                Logger.LogError($"Can't find Collider data for {meshName}. Skipping...", LogCat.Mesh);
                return;
            }

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
                        meshCollider.sharedMesh = GetBoxMesh(meshName, i, coll);
                        break;
                    default:
                        Logger.LogError($"Collider cache type {coll.T} not yet handled", LogCat.Mesh);
                        break;
                }
            }
        }

        private Mesh GetBoxMesh(string meshName, int index, VobItemColliderCacheCreatorDomain.Data coll)
        {
            var cacheKey = $"{meshName}_Collider{index}";
            if (_multiTypeCacheService.Meshes.TryGetValue(cacheKey, out var boxMesh))
                return boxMesh;

            var half = coll.S * 0.5f;
            var corners = new List<Vector3>(8);
            for (var i = 0; i < 8; i++)
            {
                var local = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);
                corners.Add(coll.C + coll.Q * local);
            }

            boxMesh = new Mesh { name = cacheKey };
            boxMesh.SetVertices(corners);
            boxMesh.SetTriangles(_boxTriangles, 0);
            boxMesh.RecalculateBounds();

            _multiTypeCacheService.Meshes.Add(cacheKey, boxMesh);
            return boxMesh;
        }
    }
}
