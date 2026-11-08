using System;
using System.Collections.Generic;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Services.Caches;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// VRBow's look at a Gothic bow mesh, done once per bow: the limb tips (ends of the long axis), the grip (the vertex
    /// farthest from the string), the max draw, a mesh copy without the Gothic string and the draw frames of its
    /// S_SHOOT morph animation. Mesh-local values; VRBow places them in the world.
    /// </summary>
    public class VrBowMeshCalibration
    {
        public MeshFilter MeshFilter { get; private set; }
        public Mesh OriginalMesh { get; private set; }

        /// <summary>
        /// The copy without the Gothic string - VRBow bends it with the draw.
        /// </summary>
        public Mesh BowMesh { get; private set; }

        public Vector3[] RestVertices { get; private set; }

        /// <summary>
        /// The morph frames of the draw (vertex positions per frame), null if the bow has none that fit.
        /// </summary>
        public List<Vector3[]> DrawFrames { get; private set; }

        public int TipTopIndex { get; private set; }
        public int TipBottomIndex { get; private set; }
        public Vector3 LocalGrip { get; private set; }

        /// <summary>
        /// From the string towards the grip: where the arrow flies.
        /// </summary>
        public Vector3 LocalForward { get; private set; }

        /// <summary>
        /// Meters the string can be drawn back.
        /// </summary>
        public float MaxDraw { get; private set; }

        /// <summary>
        /// Finds the bow mesh below root and calibrates it. On success the mesh filter shows BowMesh.
        /// </summary>
        public bool TrySetup(GameObject root, ItemInstance item, MorphMeshCacheService morphMeshCacheService)
        {
            foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = meshFilter.sharedMesh;
                if (mesh != null && mesh.isReadable && mesh.vertexCount > 0)
                {
                    MeshFilter = meshFilter;
                    break;
                }
            }
            if (MeshFilter == null)
                return false;

            OriginalMesh = MeshFilter.sharedMesh;
            RestVertices = OriginalMesh.vertices;

            var bounds = OriginalMesh.bounds;
            var size = bounds.size;
            var axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;

            var tipTopIndex = 0;
            var tipBottomIndex = 0;
            for (var i = 1; i < RestVertices.Length; i++)
            {
                if (RestVertices[i][axis] > RestVertices[tipTopIndex][axis])
                    tipTopIndex = i;
                if (RestVertices[i][axis] < RestVertices[tipBottomIndex][axis])
                    tipBottomIndex = i;
            }
            TipTopIndex = tipTopIndex;
            TipBottomIndex = tipBottomIndex;

            var tipTop = RestVertices[TipTopIndex];
            var tipBottom = RestVertices[TipBottomIndex];
            var bowLength = Vector3.Distance(tipTop, tipBottom);
            if (bowLength < VRConstants.Ranged.BowMinLength)
                return false;

            // Grip/belly: the vertex farthest away from the string line.
            var farthest = 0f;
            var localGrip = bounds.center;
            foreach (var vertex in RestVertices)
            {
                var distance = DistanceToSegment(vertex, tipTop, tipBottom, out _);
                if (distance > farthest)
                {
                    farthest = distance;
                    localGrip = vertex;
                }
            }
            LocalGrip = localGrip;
            DistanceToSegment(LocalGrip, tipTop, tipBottom, out var gripOnString);
            LocalForward = (LocalGrip - gripOnString).normalized;

            var worldScale = MeshFilter.transform.lossyScale.x;
            MaxDraw = Mathf.Clamp(bowLength * worldScale * VRConstants.Ranged.BowDrawPerLength,
                VRConstants.Ranged.BowMinDraw, VRConstants.Ranged.BowMaxDraw);

            BowMesh = Object.Instantiate(OriginalMesh);
            BowMesh.name = $"{OriginalMesh.name} (VR bow)";
            BowMesh.MarkDynamic();
            var removed = RemoveStringTriangles(BowMesh, tipTop, tipBottom, bowLength);
            MeshFilter.sharedMesh = BowMesh;

            DrawFrames = TryGetDrawFrames(item, morphMeshCacheService);
            Logger.Log($"[VRBow] {item.Name}: length={bowLength:F2}, string triangles removed={removed}", LogCat.VR);
            return true;
        }

        public static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b, out Vector3 closest)
        {
            return DistanceToSegment(point, a, b, out closest, out _);
        }

        public static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b, out Vector3 closest, out float t)
        {
            var ab = b - a;
            t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude);
            closest = a + ab * t;
            return Vector3.Distance(point, closest);
        }

        /// <summary>
        /// The Gothic string = triangles lying on the line between the limb tips (not at the tips themselves).
        /// </summary>
        private int RemoveStringTriangles(Mesh mesh, Vector3 tipTop, Vector3 tipBottom, float bowLength)
        {
            var maxDistance = Mathf.Max(0.005f, bowLength * 0.015f);
            var removed = 0;

            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                var triangles = mesh.GetTriangles(subMesh);
                var kept = new List<int>(triangles.Length);
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = RestVertices[triangles[i]];
                    var b = RestVertices[triangles[i + 1]];
                    var c = RestVertices[triangles[i + 2]];
                    DistanceToSegment((a + b + c) / 3f, tipTop, tipBottom, out _, out var t);

                    var isString = t > 0.08f && t < 0.92f &&
                                   DistanceToSegment(a, tipTop, tipBottom, out _) < maxDistance &&
                                   DistanceToSegment(b, tipTop, tipBottom, out _) < maxDistance &&
                                   DistanceToSegment(c, tipTop, tipBottom, out _) < maxDistance;
                    if (isString)
                    {
                        removed++;
                        continue;
                    }

                    kept.Add(triangles[i]);
                    kept.Add(triangles[i + 1]);
                    kept.Add(triangles[i + 2]);
                }
                mesh.SetTriangles(kept, subMesh);
            }

            return removed;
        }

        /// <summary>
        /// Gothic bows: morphAni "S_SHOOT" = the 10 frames of the draw (ITRW_BOW_*.MMB). Only if the vertex layout
        /// matches this mesh - otherwise the limbs simply don't bend.
        /// </summary>
        private List<Vector3[]> TryGetDrawFrames(ItemInstance item, MorphMeshCacheService morphMeshCacheService)
        {
            try
            {
                // By position: the cached mapping was missing for every bow (built once without the morph mesh) -
                // morph frames=0, the limbs never bent.
                var frames = morphMeshCacheService.TryGetMorphDataForVertices(item.Visual,
                    Constants.Animations.BowShootMorphAnimName, RestVertices);
                if (frames == null || frames.Count == 0 || frames[0].Length != RestVertices.Length)
                    return null;
                return frames;
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[VRBow] {item.Visual}: no {Constants.Animations.BowShootMorphAnimName} morph " +
                                  $"({e.Message}) - limbs stay straight.", LogCat.VR);
                return null;
            }
        }
    }
}
