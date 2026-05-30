using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GUZ.Core.Adapters.UI.LoadingBars;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Manager;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using Reflex.Attributes;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Domain.StaticCache
{
    public class VobItemColliderCacheCreatorDomain
    {
        public Dictionary<string, List<Data>> ItemCollider { get; } = new();
        
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly FrameSkipperService _frameSkipperService;
        [Inject] private readonly LoadingService _loadingService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        
        [Serializable]
        public struct Data // ColliderData
        {
            public T T; // Type
            public Vector3 C; // BoxCapsuleCenter
            public Vector3 S; // BoxSize
            public int D; // CapsuleDirection
            public float H; // CapsuleHeight
            public float R; // CapsuleRadius
        };
        
        public enum T // ColliderType
        {
            B, // Box
            C // Capsule
        }
        
        private class WidthSegment
        {
            public float MinHeight, MaxHeight;      // Height bounds along the weapon's main axis
            public float AverageWidth;              // Average width at this height
            public List<Vector3> Vertices = new();  // Vertices in this segment
            public Vector3 Center;                  // Center point
            public Vector3 Size;                    // Bounding box size
            public T SuggestedType;      // Suggested collider type
        }
        
        
        public async Task CalculateVobItemColliderCache(Dictionary<string, Bounds> cachedVobBounds)
        {
            var allItems = _gameStateService.GothicVm.GetInstanceSymbols("C_Item");
            
            _loadingService.SetPhase(nameof(PreCachingLoadingBarHandler.ProgressTypesGlobal.CalculateVobItemCollider), allItems.Count);
            
            foreach (var obj in allItems)
            {
                await _frameSkipperService.TrySkipToNextFrame();
                _loadingService.Tick();

                var item = _vmCacheService.TryGetItemData(obj.Name);
                if (item == null)
                    continue;

                // Already calculated
                if (ItemCollider.ContainsKey(item.Visual))
                    continue;
                
                ItemCollider.Add(item.Visual, new());
                
                GenerateItemColliders(item.Visual, cachedVobBounds);
            }
        }

        /// <summary>
        /// Generate colliders based on width variations along the items' main axis
        /// Automatically detects weapon orientation and segments from handle to tip
        /// </summary>
        public void GenerateItemColliders(string visualName, Dictionary<string, Bounds> cachedVobBounds, float widthThreshold = 0.4f, int minVerticesPerSegment = 3,
            int samples = 50, float segmentDistance = 0.05f)
        {
            var mrm = _resourceCacheService.TryGetMultiResolutionMesh(visualName)?.Cache();
            if (mrm == null)
                return;

            var bounds = cachedVobBounds[visualName];
            if (bounds == default)
                return;

            // Extract vertices and triangles similar to PrepareMeshFilter
            var calculatedVertices = mrm.Positions;
            
            var triangleCount = mrm.SubMeshes.Sum(i => i.Triangles.Count);
            var vertexCount = triangleCount * 3;
            
            var vertices = new Vector3[vertexCount];
            var triangles = new int[vertexCount];
            var index = 0;

            foreach (var subMesh in mrm.SubMeshes)
            {
                for (var i = 0; i < subMesh.Triangles.Count; i++)
                {
                    // One triangle is made of 3 elements for Unity. We therefore need to prepare 3 elements within one loop.
                    var wedge2Index = subMesh.Wedges[subMesh.Triangles[i].Wedge2].Index;
                    var wedge1Index = subMesh.Wedges[subMesh.Triangles[i].Wedge1].Index;
                    var wedge0Index = subMesh.Wedges[subMesh.Triangles[i].Wedge0].Index;

                    vertices[index] = calculatedVertices[wedge2Index].ToUnityVector();
                    triangles[index] = index++;
                    vertices[index] = calculatedVertices[wedge1Index].ToUnityVector();
                    triangles[index] = index++;
                    vertices[index] = calculatedVertices[wedge0Index].ToUnityVector();
                    triangles[index] = index++;
                }
            }

            if (vertices.Length == 0 || triangles.Length == 0)
                return;
            
            // Logger.LogEditor($"Calculating Colliders for {visualName}", LogCat.PreCaching);
            
            // Step 1: Determine the weapon's main axis and orientation
            var weaponOrientation = DetermineWeaponOrientation(vertices, bounds);

            // Step 2: Create width profile along the main axis using triangles
            var widthProfile = CreateWidthProfile(vertices, triangles, bounds, weaponOrientation, samples);

            // Step 3: Segment based on significant width changes
            var segments = CreateWidthBasedSegments(vertices, triangles, widthProfile, weaponOrientation, widthThreshold, minVerticesPerSegment, segmentDistance);

            // Step 4: Create appropriate colliders for each segment
            CreateCollidersFromSegments(visualName, segments, weaponOrientation);

            // Logger.LogEditor($"Created {segments.Count} colliders for {visualName} using {weaponOrientation.mainAxis} axis (bounds: {bounds.size})", LogCat.PreCaching);
        }
        
        private struct WeaponOrientation
        {
            public int mainAxis;           // 0=X, 1=Y, 2=Z
            public float minValue;         // Minimum value along main axis
            public float maxValue;         // Maximum value along main axis
            public float length;           // Length along main axis
        }
        
        private WeaponOrientation DetermineWeaponOrientation(Vector3[] vertices, Bounds bounds)
        {
            var orientation = new WeaponOrientation();
            var size = bounds.size;
            
            // Find the longest axis - this should be the weapon's main axis
            if (size.x >= size.y && size.x >= size.z)
            {
                orientation.mainAxis = 0; // X-axis
                orientation.minValue = bounds.min.x;
                orientation.maxValue = bounds.max.x;
                orientation.length = size.x;
            }
            else if (size.y >= size.x && size.y >= size.z)
            {
                orientation.mainAxis = 1; // Y-axis
                orientation.minValue = bounds.min.y;
                orientation.maxValue = bounds.max.y;
                orientation.length = size.y;
            }
            else
            {
                orientation.mainAxis = 2; // Z-axis
                orientation.minValue = bounds.min.z;
                orientation.maxValue = bounds.max.z;
                orientation.length = size.z;
            }
            
            // Additional validation: For weapons, length should be significantly longer than width
            var maxWidth = orientation.mainAxis == 0 ? Mathf.Max(size.y, size.z) : 
                            orientation.mainAxis == 1 ? Mathf.Max(size.x, size.z) : 
                                                        Mathf.Max(size.x, size.y);
            
            var aspectRatio = orientation.length / maxWidth;
            
            if (aspectRatio < 1.5f)
                Logger.LogWarning($"Weapon aspect ratio is low ({aspectRatio:F2}). This might not be a typical weapon shape.", LogCat.PreCaching);

            // Logger.LogEditor($"Weapon orientation: {orientation.mainAxis} axis, length: {orientation.length:F3}, aspect ratio: {aspectRatio:F2}", LogCat.PreCaching);
            
            return orientation;
        }
        
        private List<float> CreateWidthProfile(Vector3[] vertices, int[] triangles, Bounds bounds, WeaponOrientation orientation, int samples)
        {
            var profile = new List<float>(samples);
            var step = orientation.length / samples;
            
            for (var i = 0; i < samples; i++)
            {
                var currentPos = orientation.minValue + (i * step);
                var nextPos = orientation.minValue + ((i + 1) * step);

                // Find all triangles that intersect with this slice
                var intersectionPoints = GetSliceTriangleIntersections(vertices, triangles, orientation.mainAxis, currentPos, nextPos);
                
                if (intersectionPoints.Count == 0)
                {
                    // Use interpolated value from neighboring slices if no intersections found
                    var interpolatedWidth = InterpolateWidth(profile, i);
                    profile.Add(interpolatedWidth);
                    continue;
                }
                
                // Calculate width at this slice using intersection points
                var width = CalculateSliceWidthFromPoints(intersectionPoints, orientation.mainAxis);
                profile.Add(width);
            }
            
            return profile;
        }

        private List<Vector3> GetSliceTriangleIntersections(Vector3[] vertices, int[] triangles, int mainAxis, float sliceStart, float sliceEnd)
        {
            var intersectionPoints = new List<Vector3>();
            
            // Process triangles in groups of 3
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var v1 = vertices[triangles[i]];
                var v2 = vertices[triangles[i + 1]];
                var v3 = vertices[triangles[i + 2]];
                
                // Check if triangle intersects with the slice
                var trianglePoints = new List<Vector3> { v1, v2, v3 };
                var sliceIntersections = GetTriangleSliceIntersection(trianglePoints, mainAxis, sliceStart, sliceEnd);
                intersectionPoints.AddRange(sliceIntersections);
            }
            
            return intersectionPoints;
        }

        private List<Vector3> GetTriangleSliceIntersection(List<Vector3> triangle, int mainAxis, float sliceStart, float sliceEnd)
        {
            var intersectionPoints = new List<Vector3>();
            
            // Add vertices that fall within the slice
            foreach (var vertex in triangle)
            {
                var axisValue = GetAxisValue(vertex, mainAxis);
                if (axisValue >= sliceStart && axisValue < sliceEnd)
                {
                    intersectionPoints.Add(vertex);
                }
            }
            
            // Find edge intersections with slice boundaries
            for (var i = 0; i < triangle.Count; i++)
            {
                var v1 = triangle[i];
                var v2 = triangle[(i + 1) % triangle.Count];
                
                var v1Axis = GetAxisValue(v1, mainAxis);
                var v2Axis = GetAxisValue(v2, mainAxis);
                
                // Check intersection with slice start
                var startIntersection = GetLineSliceIntersection(v1, v2, v1Axis, v2Axis, sliceStart);
                if (startIntersection.HasValue)
                    intersectionPoints.Add(startIntersection.Value);
                
                // Check intersection with slice end
                var endIntersection = GetLineSliceIntersection(v1, v2, v1Axis, v2Axis, sliceEnd);
                if (endIntersection.HasValue)
                    intersectionPoints.Add(endIntersection.Value);
            }
            
            return intersectionPoints;
        }

        private Vector3? GetLineSliceIntersection(Vector3 v1, Vector3 v2, float v1Axis, float v2Axis, float slicePosition)
        {
            // Check if the edge crosses the slice plane
            if ((v1Axis <= slicePosition && v2Axis >= slicePosition) || 
                (v1Axis >= slicePosition && v2Axis <= slicePosition))
            {
                if (Mathf.Abs(v2Axis - v1Axis) < 0.001f) return null; // Avoid division by zero
                
                // Linear interpolation to find intersection point
                var t = (slicePosition - v1Axis) / (v2Axis - v1Axis);
                return Vector3.Lerp(v1, v2, t);
            }
            
            return null;
        }

        private float CalculateSliceWidthFromPoints(List<Vector3> points, int mainAxis)
        {
            if (points.Count == 0) return 0f;
            
            // Remove duplicate points
            var uniquePoints = points.Distinct().ToList();
            if (uniquePoints.Count == 0) return 0f;
            
            // Calculate width in the two perpendicular axes
            var perpValues1 = new List<float>();
            var perpValues2 = new List<float>();
            
            foreach (var point in uniquePoints)
            {
                switch (mainAxis)
                {
                    case 0: // Main axis is X, so measure Y and Z
                        perpValues1.Add(point.y);
                        perpValues2.Add(point.z);
                        break;
                    case 1: // Main axis is Y, so measure X and Z
                        perpValues1.Add(point.x);
                        perpValues2.Add(point.z);
                        break;
                    case 2: // Main axis is Z, so measure X and Y
                        perpValues1.Add(point.x);
                        perpValues2.Add(point.y);
                        break;
                }
            }
            
            var width1 = perpValues1.Count > 0 ? perpValues1.Max() - perpValues1.Min() : 0f;
            var width2 = perpValues2.Count > 0 ? perpValues2.Max() - perpValues2.Min() : 0f;
            
            return Mathf.Max(width1, width2);
        }
        
        private float GetAxisValue(Vector3 vertex, int axis)
        {
            return axis == 0 ? vertex.x : (axis == 1 ? vertex.y : vertex.z);
        }
        
        private float InterpolateWidth(List<float> profile, int index)
        {
            if (profile.Count == 0) return 0f;
            
            // Find the last valid width value
            for (var i = index - 1; i >= 0; i--)
            {
                if (profile[i] > 0f)
                    return profile[i];
            }
            
            return 0f;
        }

        private float CalculateSliceWidth(List<Vector3> sliceVertices, int mainAxis)
        {
            if (sliceVertices.Count == 0) return 0f;
            
            // Calculate width in the two perpendicular axes
            var perpValues1 = new List<float>();
            var perpValues2 = new List<float>();
            
            foreach (var vertex in sliceVertices)
            {
                switch (mainAxis)
                {
                    case 0: // Main axis is X, so measure Y and Z
                        perpValues1.Add(vertex.y);
                        perpValues2.Add(vertex.z);
                        break;
                    case 1: // Main axis is Y, so measure X and Z
                        perpValues1.Add(vertex.x);
                        perpValues2.Add(vertex.z);
                        break;
                    case 2: // Main axis is Z, so measure X and Y
                        perpValues1.Add(vertex.x);
                        perpValues2.Add(vertex.y);
                        break;
                }
            }
            
            var width1 = perpValues1.Count > 0 ? perpValues1.Max() - perpValues1.Min() : 0f;
            var width2 = perpValues2.Count > 0 ? perpValues2.Max() - perpValues2.Min() : 0f;
            
            // Return the maximum width (considering the weapon could be rotated around the main axis)
            return Mathf.Max(width1, width2);
        }
        
        private List<WidthSegment> CreateWidthBasedSegments(Vector3[] vertices, int[] triangles, List<float> widthProfile,
            WeaponOrientation orientation, float threshold, int minVerticesPerSegment, float segmentDistance)
        {
            var segments = new List<WidthSegment>();
            
            // Find significant width changes
            var segmentBoundaries = new List<float> { orientation.minValue };
            
            for (var i = 1; i < widthProfile.Count - 1; i++)
            {
                var currentWidth = widthProfile[i];
                var previousWidth = widthProfile[i - 1];
                var nextWidth = widthProfile[i + 1];
                
                if (previousWidth <= 0 || currentWidth <= 0) continue;
                
                // Check for significant width change (shrinking or expanding)
                var significantChange = false;
                
                var changeRatio = Mathf.Abs(currentWidth - previousWidth) / previousWidth;
                if (changeRatio > threshold)
                {
                    significantChange = true;
                }
                
                // Also check for local minima/maxima (typical at guard/pommel transitions)
                if (currentWidth < previousWidth && currentWidth < nextWidth) // Local minimum
                    significantChange = true;
                if (currentWidth > previousWidth && currentWidth > nextWidth && currentWidth > threshold * 2) // Local maximum
                    significantChange = true;
                
                if (significantChange)
                {
                    var boundary = orientation.minValue + ((float)i / widthProfile.Count) * orientation.length;
                    segmentBoundaries.Add(boundary);

                    // Logger.LogEditor($"Found width change at position {boundary:F3}: {previousWidth:F3} -> {currentWidth:F3} (ratio: {changeRatio:F3})", LogCat.PreCaching);
                }
            }
            
            segmentBoundaries.Add(orientation.maxValue);
            
            // Ensure minimum distance between boundaries
            var filteredBoundaries = FilterCloseSegments(segmentBoundaries, orientation.length * segmentDistance);
            
            // Create segments based on boundaries
            for (var i = 0; i < filteredBoundaries.Count - 1; i++)
            {
                var segment = new WidthSegment
                {
                    MinHeight = filteredBoundaries[i],
                    MaxHeight = filteredBoundaries[i + 1]
                };
    
                // Assign vertices to this segment using triangle-based approach
                var segmentPoints = GetSliceTriangleIntersections(vertices, triangles, orientation.mainAxis, 
                    segment.MinHeight, segment.MaxHeight);
    
                // Also include original vertices that fall within the segment
                foreach (var vertex in vertices)
                {
                    var axisValue = GetAxisValue(vertex, orientation.mainAxis);
                    if (axisValue >= segment.MinHeight && axisValue < segment.MaxHeight)
                    {
                        segmentPoints.Add(vertex);
                    }
                }
    
                // Remove duplicates and assign to segment
                segment.Vertices = segmentPoints.Distinct().ToList();
    
                // Only keep segments with enough points (lower threshold since we have more points now)
                if (segment.Vertices.Count >= minVerticesPerSegment / 3) // Reduced threshold
                {
                    CalculateSegmentProperties(segment, orientation.mainAxis);
                    segments.Add(segment);
                }
            }
            
            return segments;
        }
        
        private List<float> FilterCloseSegments(List<float> boundaries, float minDistance)
        {
            if (boundaries.Count <= 2) return boundaries;
            
            var filtered = new List<float> { boundaries[0] };
            
            for (var i = 1; i < boundaries.Count - 1; i++)
            {
                if (boundaries[i] - filtered[filtered.Count - 1] >= minDistance)
                {
                    filtered.Add(boundaries[i]);
                }
            }
            
            filtered.Add(boundaries[boundaries.Count - 1]);
            return filtered;
        }
        
        private void CalculateSegmentProperties(WidthSegment segment, int mainAxis)
        {
            if (segment.Vertices.Count == 0)
                return;
            
            var segmentBounds = CalculateBounds(segment.Vertices);
            segment.Center = segmentBounds.center;
            segment.Size = segmentBounds.size;
            
            // Calculate average width
            segment.AverageWidth = CalculateSliceWidth(segment.Vertices, mainAxis);
            
            // Determine collider type based on shape
            var height = GetAxisSize(segment.Size, mainAxis);
            var maxWidth = GetMaxPerpendicularSize(segment.Size, mainAxis);
            
            // Use capsule for long, thin segments (blade), box for wider segments (guard, pommel)
            var aspectRatio = height / (maxWidth + 0.001f); // Avoid division by zero
            segment.SuggestedType = aspectRatio > 2.0f ? T.C : T.B;
            
            // Logger.LogEditor($"Segment properties: Height={height:F3}, Width={maxWidth:F3}, AspectRatio={aspectRatio:F2}, Type={segment.SuggestedType}", LogCat.PreCaching);
        }
        
        private float GetAxisSize(Vector3 size, int axis)
        {
            return axis == 0 ? size.x : (axis == 1 ? size.y : size.z);
        }
        
        private float GetMaxPerpendicularSize(Vector3 size, int mainAxis)
        {
            switch (mainAxis)
            {
                case 0: return Mathf.Max(size.y, size.z);
                case 1: return Mathf.Max(size.x, size.z);
                case 2: return Mathf.Max(size.x, size.y);
                default: return 0f;
            }
        }
        
        private void CreateCollidersFromSegments(string cacheKey, List<WidthSegment> segments, WeaponOrientation orientation)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];

                if (segment.SuggestedType == T.C)
                    CreateCapsuleCollider(cacheKey, segment, orientation.mainAxis);
                else
                    CreateBoxCollider(cacheKey, segment);
                
                // Logger.LogEditor($"Segment {i}: {segment.SuggestedType}, Width: {segment.AverageWidth:F3}, Height: {segment.MaxHeight - segment.MinHeight:F3}, Vertices: {segment.Vertices.Count}", LogCat.PreCaching);
            }
        }
        
        private void CreateBoxCollider(string cacheKey, WidthSegment segment)
        {
            ItemCollider[cacheKey].Add(new()
            {
                T = T.B,
                C = segment.Center,
                S = segment.Size
            });
        }
        
        private void CreateCapsuleCollider(string cacheKey, WidthSegment segment, int mainAxis)
        {
            // Set dimensions based on the main axis
            var height = GetAxisSize(segment.Size, mainAxis);
            var radius = GetMaxPerpendicularSize(segment.Size, mainAxis) * 0.5f;
            
            ItemCollider[cacheKey].Add(new()
            {
                T = T.C,
                C = segment.Center,
                // Set the capsule direction to match the items main axis
                D = mainAxis,
                H = height,
                R = radius
            });
        }
        
        private Bounds CalculateBounds(List<Vector3> vertices)
        {
            if (vertices.Count == 0)
                return new Bounds();
            
            var min = vertices[0];
            var max = vertices[0];
            
            foreach (var vertex in vertices)
            {
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }
            
            return new Bounds((min + max) * 0.5f, max - min);
        }
    }
}
