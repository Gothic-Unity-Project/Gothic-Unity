using System.Collections.Generic;
using UnityEngine;

namespace Gothic.Core.Domain.Meshes
{
    /// <summary>
    /// Axis aligned boxes along an item mesh, for items without pre-cached colliders (morph meshes) and bows or
    /// crossbows the fit (ItemColliderFittingDomain) found no part of. Morph mesh items without colliders fell
    /// through the ground, couldn't be force-grabbed and flew away when rotated in a hand; a box around the whole bow
    /// enclosed the holding hand, physics pushed hand and bow away.
    /// The mesh is cut into slices along its long axis, the vertices of a slice are split into clusters (gaps > 3 cm)
    /// and each cluster gets a box. So the hand can grab every part (crossbow stock and prod, bow grip and limbs) and
    /// nothing encloses empty space. Hair-thin clusters (a bow's string) get no box - the string is drawn, not
    /// grabbed.
    /// Pure math, the builder (VobItemColliderBuilder) caches the result per mesh.
    /// </summary>
    public static class ItemColliderSlicingDomain
    {
        private const int _colliderSlices = 8;
        private const int _bowColliderSlices = 14;
        private const float _colliderClusterGap = 0.03f;
        private const float _bowColliderClusterGap = 0.02f;
        private const float _colliderMinThickness = 0.012f;
        private const float _colliderMinSize = 0.02f;
        // Meters: shorter items get no slices (the caller adds a capsule).
        private const float _minLength = 0.05f;


        /// <summary>
        /// vertices: the item's mesh vertices in meters. isBow: the string is left out. Empty if the mesh is too
        /// small or has no vertices.
        /// </summary>
        public static List<Bounds> Slice(Vector3[] vertices, bool isBow)
        {
            var boxes = new List<Bounds>();
            if (vertices == null || vertices.Length == 0)
                return boxes;

            var bounds = GetBounds(vertices);
            var size = bounds.size;
            var axis = GetLongestAxis(size);
            var length = size[axis];
            if (length < _minLength)
                return boxes;

            // Bows: the string runs from tip to tip (ends of the long axis) - it's drawn, never grabbed. Without its
            // vertices the boxes follow the limbs only (with them they became one flat plate).
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

                        var boxSize = Vector3.Max(max - min, Vector3.one * _colliderMinSize);
                        boxes.Add(new Bounds((min + max) * 0.5f, boxSize));
                    }
                }
            }

            return boxes;
        }

        /// <summary>
        /// 0, 1 or 2: x, y or z.
        /// </summary>
        public static int GetLongestAxis(Vector3 size)
        {
            return size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        }

        private static Bounds GetBounds(Vector3[] vertices)
        {
            var min = vertices[0];
            var max = vertices[0];
            foreach (var vertex in vertices)
            {
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }
            return new Bounds((min + max) * 0.5f, max - min);
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
