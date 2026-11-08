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
    /// Adds an item's pre-cached colliders (VobItemColliderCacheCreatorDomain). The oriented boxes of melee weapons
    /// become convex box meshes on the item itself: child objects with BoxColliders would keep their layer when the
    /// grabbed item switches to VobItemNoWorldCollision. The box meshes are shared like the item meshes
    /// (MultiTypeCacheService).
    /// Morph mesh items (bows, crossbows) have no pre-cached colliders (the cache is calculated from MRMs only): their
    /// boxes come from their mesh when they spawn.
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


        // Each box is one collider on the weapon, which PhysX checks on every step it touches something.
        private const int _maxFittedBoxes = 12;

        // One warning per mesh without cached colliders.
        private static readonly HashSet<string> _meshesWithoutCachedColliders = new();


        /// <summary>
        /// kind: from the item's flags (bow, crossbow or anything else). mesh: the item's rendered mesh - bows and
        /// crossbows are fitted to it, items without cached colliders get boxes along it.
        /// </summary>
        public void AddColliders(GameObject rootGo, string meshName, ItemColliderKind kind, Mesh mesh = null)
        {
            if (mesh != null && kind != ItemColliderKind.Default &&
                TryAddFittedRangedWeaponColliders(rootGo, meshName, kind, mesh))
                return;

            if (!_staticCacheService.LoadedVobItemColliders.TryGetValue(meshName, out var colliders) ||
                colliders.Count == 0)
            {
                if (mesh != null)
                {
                    AddMeshBoxColliders(rootGo, meshName, kind, mesh);
                    return;
                }

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
            return GetBoxMesh($"{meshName}_Collider{index}", coll.C, coll.S, coll.Q);
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
        /// Bows and crossbows (ITEM_KAT_FF) get boxes fitted to their parts: stock, prod, limbs, no string. Boxes along
        /// the mesh axes follow the bounds of a straight weapon - a curved bow got three fat capsules, 13 cm radius,
        /// full of air; axis aligned slices missed the diagonal crossbow prods. Arrows and bolts (ITEM_KAT_MUN) are
        /// thin shafts the fitting drops as "strings" - they keep their cached colliders. The fit runs once per mesh,
        /// its box meshes are shared (MultiTypeCacheService).
        /// </summary>
        private bool TryAddFittedRangedWeaponColliders(GameObject rootGo, string meshName, ItemColliderKind kind,
            Mesh mesh)
        {
            var boxMeshes = new List<Mesh>();
            if (_multiTypeCacheService.Meshes.TryGetValue(GetFittedCacheKey(meshName, 0), out var firstBoxMesh))
            {
                boxMeshes.Add(firstBoxMesh);
                while (_multiTypeCacheService.Meshes.TryGetValue(GetFittedCacheKey(meshName, boxMeshes.Count),
                           out var boxMesh))
                    boxMeshes.Add(boxMesh);
            }
            else if (mesh.isReadable)
            {
                var boxes = ItemColliderFittingDomain.Fit(mesh.vertices, mesh.triangles, false, _maxFittedBoxes,
                    out var isTrimmed);
                if (isTrimmed)
                    Logger.LogWarning($"{meshName} has more than {_maxFittedBoxes} separate parts. " +
                                      "The smallest get no collider.", LogCat.Mesh);

                for (var i = 0; i < boxes.Count; i++)
                    boxMeshes.Add(GetBoxMesh(GetFittedCacheKey(meshName, i), boxes[i].Center, boxes[i].Size,
                        boxes[i].Rotation));
                Logger.Log($"[MeshBuilder] {meshName}: {boxes.Count} fitted colliders", LogCat.Mesh);
            }

            foreach (var boxMesh in boxMeshes)
            {
                var meshCollider = rootGo.AddComponent<MeshCollider>();
                meshCollider.convex = true;
                meshCollider.sharedMesh = boxMesh;
            }
            return boxMeshes.Count > 0 || AddSlicedBoxColliders(rootGo, kind, mesh);
        }

        private static string GetFittedCacheKey(string meshName, int index)
        {
            return $"{meshName}_FitCollider{index}";
        }

        /// <summary>
        /// Morph mesh items without cached colliders fell through the ground, couldn't be force-grabbed and flew away
        /// when rotated in a hand. Thin boxes along the mesh itself - a box around the whole bow enclosed the holding
        /// hand, physics pushed hand and bow away. A thin capsule along the mesh if it can't be sliced.
        /// </summary>
        private static void AddMeshBoxColliders(GameObject rootGo, string meshName, ItemColliderKind kind, Mesh mesh)
        {
            if (_meshesWithoutCachedColliders.Add(meshName))
                Logger.LogWarning($"No cached colliders for {meshName} - boxes from its mesh.", LogCat.Mesh);

            if (AddSlicedBoxColliders(rootGo, kind, mesh))
                return;

            var size = mesh.bounds.size;
            var axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var fallback = rootGo.AddComponent<CapsuleCollider>();
            fallback.center = mesh.bounds.center;
            fallback.direction = axis;
            fallback.height = Mathf.Max(size[axis], 0.1f);
            fallback.radius = 0.025f;
        }

        private const int _colliderSlices = 8;
        private const int _bowColliderSlices = 14;
        private const float _colliderClusterGap = 0.03f;
        private const float _bowColliderClusterGap = 0.02f;
        private const float _colliderMinThickness = 0.012f;
        private const float _colliderMinSize = 0.02f;

        /// <summary>
        /// Morph mesh items (bows, crossbows) without cached colliders: the mesh is cut into slices along its long axis,
        /// the vertices of a slice are split into clusters (gaps > 3 cm) and each cluster gets a box. So the hand can
        /// grab every part (crossbow stock and prod, bow grip and limbs) and nothing encloses empty space.
        /// Hair-thin clusters (a bow's string) get no box - the string is drawn, not grabbed.
        /// </summary>
        private static bool AddSlicedBoxColliders(GameObject rootGo, ItemColliderKind kind, Mesh mesh)
        {
            if (!mesh.isReadable)
                return false;

            var vertices = mesh.vertices;
            var bounds = mesh.bounds;
            var size = bounds.size;
            var axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var length = size[axis];
            if (vertices.Length == 0 || length < 0.05f)
                return false;

            // Bows: the string runs from tip to tip (ends of the long axis) - it's drawn, never grabbed. Without its
            // vertices the boxes follow the limbs only (with them they became one flat plate).
            var isBow = kind == ItemColliderKind.Bow;
            var sliceCount = isBow ? _bowColliderSlices : _colliderSlices;
            var clusterGap = isBow ? _bowColliderClusterGap : _colliderClusterGap;
            if (isBow)
                vertices = RemoveBowString(vertices, axis, length);

            var slices = new List<Vector3>[sliceCount];
            var sliceLength = length / sliceCount;
            foreach (var vertex in vertices)
            {
                var index = Mathf.Clamp((int)((vertex[axis] - bounds.min[axis]) / sliceLength), 0, sliceCount - 1);
                (slices[index] ??= new List<Vector3>()).Add(vertex);
            }

            var axis2 = (axis + 1) % 3;
            var axis3 = (axis + 2) % 3;
            var added = 0;
            for (var i = 0; i < sliceCount; i++)
            {
                if (slices[i] == null)
                    continue;

                foreach (var cluster2 in SplitByGap(slices[i], axis2, clusterGap))
                {
                    foreach (var cluster in SplitByGap(cluster2, axis3, clusterGap))
                    {
                        var min = cluster[0];
                        var max = cluster[0];
                        foreach (var vertex in cluster)
                        {
                            min = Vector3.Min(min, vertex);
                            max = Vector3.Max(max, vertex);
                        }

                        var clusterSize = max - min;
                        if (clusterSize[axis2] < _colliderMinThickness && clusterSize[axis3] < _colliderMinThickness)
                            continue;

                        // Boxes of neighbouring slices touch - no gaps along the item.
                        min[axis] = Mathf.Min(min[axis], bounds.min[axis] + i * sliceLength);
                        max[axis] = Mathf.Max(max[axis], bounds.min[axis] + (i + 1) * sliceLength);

                        var box = rootGo.AddComponent<BoxCollider>();
                        box.center = (min + max) * 0.5f;
                        box.size = Vector3.Max(max - min, Vector3.one * _colliderMinSize);
                        added++;
                    }
                }
            }

            return added > 0;
        }

        /// <summary>
        /// Drops the vertices on the line between the two limb tips (the string), except at the tips themselves.
        /// </summary>
        private static Vector3[] RemoveBowString(Vector3[] vertices, int axis, float length)
        {
            var tipA = vertices[0];
            var tipB = vertices[0];
            foreach (var vertex in vertices)
            {
                if (vertex[axis] > tipA[axis])
                    tipA = vertex;
                if (vertex[axis] < tipB[axis])
                    tipB = vertex;
            }

            var maxDistance = Mathf.Max(0.01f, length * 0.02f);
            var line = tipB - tipA;
            var kept = new List<Vector3>(vertices.Length);
            foreach (var vertex in vertices)
            {
                var t = Mathf.Clamp01(Vector3.Dot(vertex - tipA, line) / line.sqrMagnitude);
                var isString = t > 0.08f && t < 0.92f && Vector3.Distance(vertex, tipA + line * t) < maxDistance;
                if (!isString)
                    kept.Add(vertex);
            }
            return kept.Count > 0 ? kept.ToArray() : vertices;
        }

        private static List<List<Vector3>> SplitByGap(List<Vector3> points, int axis, float gap)
        {
            points.Sort((a, b) => a[axis].CompareTo(b[axis]));
            var clusters = new List<List<Vector3>> { new() { points[0] } };
            for (var i = 1; i < points.Count; i++)
            {
                if (points[i][axis] - points[i - 1][axis] > gap)
                    clusters.Add(new List<Vector3>());
                clusters[^1].Add(points[i]);
            }
            return clusters;
        }
    }
}
