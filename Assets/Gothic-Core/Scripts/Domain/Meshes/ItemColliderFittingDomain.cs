using System.Collections.Generic;
using UnityEngine;

namespace Gothic.Core.Domain.Meshes
{
    /// <summary>
    /// DeveloperConfig.EnableRangedFittedColliders: oriented boxes that follow an item mesh - for bows and crossbows,
    /// whose parts are curved or lie diagonally (axis aligned boxes left gaps and air).
    /// 1. The mesh is split into its separate parts (crossbow: stock, prod, trigger, string; bow: limbs, string).
    /// 2. Each part is sampled densely (its triangles, not only the few low-poly vertices) and gets a box along its
    ///    principal axes (PCA). Curved parts are halved along their long axis as long as that removes a lot of air.
    /// 3. Line-thin boxes (bow and crossbow strings) are left out - the string is drawn, not grabbed.
    /// Pure math, cached per mesh by the caller.
    /// </summary>
    public static class ItemColliderFittingDomain
    {
        public struct Box
        {
            public Vector3 Center;
            public Quaternion Rotation;
            public Vector3 Size;
        }

        // Sub-divisions per triangle edge for the point cloud.
        private const int _samplesPerEdge = 6;
        private const int _maxDepth = 5;
        // A split must leave less than this share of the parent box' volume.
        private const float _splitVolumeRatio = 0.7f;
        private const float _minSplitLength = 0.05f;
        private const int _minSplitPoints = 12;
        // Meters: thinner in two directions = a string (G1 bow strings are 1.7 cm thick, the thinnest limb part 3.2 cm).
        // Every box is at least _minBoxSize thick.
        private const float _stringThickness = 0.025f;
        private const float _minBoxSize = 0.015f;
        private const float _weldPrecision = 1000f;

        /// <summary>
        /// keepThinParts: melee weapons keep line-thin parts (a poker's rod, an axe's thin shaft) - they're held there.
        /// </summary>
        public static List<Box> Fit(Vector3[] vertices, int[] triangles, bool keepThinParts = false)
        {
            var boxes = new List<Box>();
            if (vertices == null || triangles == null || triangles.Length < 3)
                return boxes;

            foreach (var part in GetParts(vertices, triangles))
                FitPart(Sample(vertices, triangles, part), 0, boxes, keepThinParts);
            return boxes;
        }

        /// <summary>
        /// Triangles grouped by connectivity (vertices welded by position - morph meshes split them per wedge).
        /// </summary>
        private static List<List<int>> GetParts(Vector3[] vertices, int[] triangles)
        {
            var ids = new Dictionary<Vector3Int, int>();
            var welded = new int[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] * _weldPrecision);
                if (!ids.TryGetValue(key, out var id))
                {
                    id = ids.Count;
                    ids[key] = id;
                }
                welded[i] = id;
            }

            var parents = new int[ids.Count];
            for (var i = 0; i < parents.Length; i++)
                parents[i] = i;

            int Find(int x)
            {
                while (parents[x] != x)
                    x = parents[x] = parents[parents[x]];
                return x;
            }

            for (var t = 0; t + 2 < triangles.Length; t += 3)
            {
                var a = Find(welded[triangles[t]]);
                parents[Find(welded[triangles[t + 1]])] = a;
                parents[Find(welded[triangles[t + 2]])] = a;
            }

            var parts = new Dictionary<int, List<int>>();
            for (var t = 0; t + 2 < triangles.Length; t += 3)
            {
                var root = Find(welded[triangles[t]]);
                if (!parts.TryGetValue(root, out var part))
                    parts[root] = part = new List<int>();
                part.Add(t);
            }
            return new List<List<int>>(parts.Values);
        }

        private static List<Vector3> Sample(Vector3[] vertices, int[] triangles, List<int> triangleStarts)
        {
            var points = new List<Vector3>();
            foreach (var t in triangleStarts)
            {
                var a = vertices[triangles[t]];
                var b = vertices[triangles[t + 1]];
                var c = vertices[triangles[t + 2]];
                for (var i = 0; i <= _samplesPerEdge; i++)
                {
                    for (var j = 0; j <= _samplesPerEdge - i; j++)
                    {
                        var u = (float)i / _samplesPerEdge;
                        var v = (float)j / _samplesPerEdge;
                        points.Add(a + (b - a) * u + (c - a) * v);
                    }
                }
            }
            return points;
        }

        private static void FitPart(List<Vector3> points, int depth, List<Box> boxes, bool keepThinParts)
        {
            if (points.Count == 0)
                return;

            var box = GetOrientedBox(points, out var axes, out var min, out var max);
            var size = max - min;
            var longAxis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;

            if (depth < _maxDepth && points.Count >= _minSplitPoints && size[longAxis] >= _minSplitLength)
            {
                var middle = (min[longAxis] + max[longAxis]) * 0.5f;
                var left = new List<Vector3>();
                var right = new List<Vector3>();
                foreach (var point in points)
                    (Vector3.Dot(point, axes[longAxis]) < middle ? left : right).Add(point);

                if (left.Count > 0 && right.Count > 0 &&
                    GetVolume(left) + GetVolume(right) < Volume(size) * _splitVolumeRatio)
                {
                    FitPart(left, depth + 1, boxes, keepThinParts);
                    FitPart(right, depth + 1, boxes, keepThinParts);
                    return;
                }
            }

            // A string: thin in two directions.
            var sorted = new[] { size.x, size.y, size.z };
            System.Array.Sort(sorted);
            if (sorted[1] < _stringThickness && !keepThinParts)
                return;

            box.Size = Vector3.Max(size, Vector3.one * _minBoxSize);
            boxes.Add(box);
        }

        private static float GetVolume(List<Vector3> points)
        {
            GetOrientedBox(points, out _, out var min, out var max);
            return Volume(max - min);
        }

        private static float Volume(Vector3 size)
        {
            // Thin parts: a minimum thickness, else a flat plate has no volume to compare.
            return Mathf.Max(size.x, _minBoxSize) * Mathf.Max(size.y, _minBoxSize) * Mathf.Max(size.z, _minBoxSize);
        }

        /// <summary>
        /// Box along the principal axes of the points (eigenvectors of their covariance).
        /// min/max are the extents along axes[0..2].
        /// </summary>
        private static Box GetOrientedBox(List<Vector3> points, out Vector3[] axes, out Vector3 min, out Vector3 max)
        {
            var mean = Vector3.zero;
            foreach (var point in points)
                mean += point;
            mean /= points.Count;

            var covariance = new float[3, 3];
            foreach (var point in points)
            {
                var d = point - mean;
                for (var r = 0; r < 3; r++)
                for (var c = 0; c < 3; c++)
                    covariance[r, c] += d[r] * d[c];
            }

            axes = GetEigenvectors(covariance);
            min = Vector3.one * float.MaxValue;
            max = Vector3.one * float.MinValue;
            foreach (var point in points)
            {
                for (var a = 0; a < 3; a++)
                {
                    var projection = Vector3.Dot(point, axes[a]);
                    min[a] = Mathf.Min(min[a], projection);
                    max[a] = Mathf.Max(max[a], projection);
                }
            }

            var centerLocal = (min + max) * 0.5f;
            var rotation = Quaternion.LookRotation(axes[2], axes[1]);
            return new Box
            {
                Center = axes[0] * centerLocal.x + axes[1] * centerLocal.y + axes[2] * centerLocal.z,
                Rotation = rotation,
                Size = max - min
            };
        }

        /// <summary>
        /// Jacobi rotations of a symmetric 3x3 matrix. Returns a right-handed orthonormal basis.
        /// </summary>
        private static Vector3[] GetEigenvectors(float[,] matrix)
        {
            var a = (float[,])matrix.Clone();
            var v = new float[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

            for (var sweep = 0; sweep < 32; sweep++)
            {
                var off = Mathf.Abs(a[0, 1]) + Mathf.Abs(a[0, 2]) + Mathf.Abs(a[1, 2]);
                if (off < 1e-9f)
                    break;

                for (var p = 0; p < 2; p++)
                {
                    for (var q = p + 1; q < 3; q++)
                    {
                        if (Mathf.Abs(a[p, q]) < 1e-12f)
                            continue;

                        var theta = (a[q, q] - a[p, p]) / (2f * a[p, q]);
                        var t = Mathf.Sign(theta) / (Mathf.Abs(theta) + Mathf.Sqrt(theta * theta + 1f));
                        if (theta == 0f)
                            t = 1f;
                        var c = 1f / Mathf.Sqrt(t * t + 1f);
                        var s = t * c;

                        for (var k = 0; k < 3; k++)
                        {
                            var akp = a[k, p];
                            var akq = a[k, q];
                            a[k, p] = c * akp - s * akq;
                            a[k, q] = s * akp + c * akq;
                        }
                        for (var k = 0; k < 3; k++)
                        {
                            var apk = a[p, k];
                            var aqk = a[q, k];
                            a[p, k] = c * apk - s * aqk;
                            a[q, k] = s * apk + c * aqk;
                        }
                        for (var k = 0; k < 3; k++)
                        {
                            var vkp = v[k, p];
                            var vkq = v[k, q];
                            v[k, p] = c * vkp - s * vkq;
                            v[k, q] = s * vkp + c * vkq;
                        }
                    }
                }
            }

            var e0 = new Vector3(v[0, 0], v[1, 0], v[2, 0]).normalized;
            var e1 = new Vector3(v[0, 1], v[1, 1], v[2, 1]).normalized;
            var e2 = Vector3.Cross(e0, e1).normalized;
            if (e0.sqrMagnitude < 0.5f || e1.sqrMagnitude < 0.5f || e2.sqrMagnitude < 0.5f)
                return new[] { Vector3.right, Vector3.up, Vector3.forward };
            e1 = Vector3.Cross(e2, e0).normalized;
            return new[] { e0, e1, e2 };
        }
    }
}
