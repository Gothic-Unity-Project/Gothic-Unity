using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gothic.Core.Adapters.UI.LoadingBars;
using Gothic.Core.Domain.StaticCache;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Const;
using Gothic.Core.Manager;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.StaticCache;
using Gothic.Core.Services.World;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;
using Mesh = UnityEngine.Mesh;
using Material = UnityEngine.Material;

namespace Gothic.Core.Domain.Meshes.Builder
{
    /// <summary>
    /// Builds all world chunk meshes via the writable MeshData API:
    /// 1. Gather per-material texture array data on the main thread (requires DI services).
    /// 2. Extract and deduplicate all chunk vertices from the cached ZenKit mesh on a background thread.
    /// 3. One Burst job writes the vertex/index buffers of all chunks in parallel.
    /// 4. Apply all meshes at once, then create GameObjects and colliders spread over frames.
    /// </summary>
    public class WorldMeshBuilder : AbstractMeshBuilder
    {
        [Inject] private readonly FrameSkipperService _frameSkipperService;
        [Inject] private readonly StationaryLightsService _stationaryLightsService;
        [Inject] private readonly RoomService _roomService;
        [Inject] private readonly Gothic.Core.Services.Config.ConfigService _worldConfigService;

        private StaticCacheService.WorldChunkContainer _worldChunks;
        private IMesh _mesh;

        // A room floor polygon: its normal at most ~45 degrees from up/down.
        private const float _floorMinNormalY = 0.7f;

        private static readonly VertexAttributeDescriptor[] _worldVertexLayout =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.Normal, VertexAttributeFormat.SNorm16, 4),
            new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4)
        };

        private static readonly VertexAttributeDescriptor[] _waterVertexLayout =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4)
        };

        /// <summary>
        /// Per-material data baked into the vertices. Gathered once on the main thread.
        /// </summary>
        private struct MaterialEntry
        {
            public int TextureArrayIndex;
            public float2 TextureScale;
            public int MaxMipLevel;
            public float4 TexAnim;
        }

        private class ChunkBuildData
        {
            public TextureCacheService.TextureArrayTypes Type;
            public WorldChunkCacheCreatorDomain.WorldChunk Chunk;

            // Expanded (pre-deduplication, worst-case) vertex count, cached during the GetPolygon sweep in
            // GatherMaterialEntries so ExtractChunks can size its buffers without a second native sweep.
            public int ExpandedVertexCount;

            // Has polygons of noCollDet materials (bushes, leaves) - its collider is built without them.
            public bool HasNoCollisionPolygons;
        }

        // Per material index: noCollDet (DeveloperConfig.EnableMaterialCollisionFilter).
        private bool[] _noCollisionMaterials;

        /// <summary>
        /// DeveloperConfig.EnableWorldLightmaps: per lightmap of the world mesh, how a vertex position (Gothic cm)
        /// becomes its lightmap uv (ZenGin zCLightMap: uv = dot(position - origin, U/V), the vectors are scaled to the
        /// page) and which slice of the lightmap array holds its page.
        /// </summary>
        private sealed class LightmapData
        {
            public float3[] Origin;
            public float3[] U;
            public float3[] V;
            public float2[] Scale;
            public int[] Slice;
            public List<byte[]> Pages;
        }

        private const int _lightmapPageSize = 256;
        private int _extractedPolygonCount;
        private int _lightmappedPolygonCount;
        private static readonly int _worldLightmapsShaderId = Shader.PropertyToID("_WorldLightmaps");
        private static readonly int _worldLightmapBrightnessShaderId = Shader.PropertyToID("_WorldLightmapBrightness");
        private static Texture2DArray _emptyLightmaps;
        private static Texture2DArray _worldLightmaps;

        private struct ExtractionBuffers : IDisposable
        {
            public NativeArray<WorldChunkMeshRange> Ranges;
            public NativeArray<WorldChunkVertex> Vertices;
            public NativeArray<WorldWaterVertex> WaterVertices;
            public NativeArray<int> Indices;

            public void Dispose()
            {
                if (Ranges.IsCreated)
                {
                    Ranges.Dispose();
                }
                if (Vertices.IsCreated)
                {
                    Vertices.Dispose();
                }
                if (WaterVertices.IsCreated)
                {
                    WaterVertices.Dispose();
                }
                if (Indices.IsCreated)
                {
                    Indices.Dispose();
                }
            }
        }

        public void SetWorldData(StaticCacheService.WorldChunkContainer worldChunks, IMesh mesh)
        {
            _worldChunks = worldChunks;
            _mesh = mesh;
        }

        public override GameObject Build()
        {
            throw new NotImplementedException("Use BuildAsync instead.");
        }

        public async Task BuildAsync([CanBeNull] LoadingService loading)
        {
            RootGo.isStatic = true;
            _stationaryLightsService.ClearWorldChunks();

            var chunks = CollectChunks(out var emptyChunkCount);

            loading?.SetPhase(nameof(WorldLoadingBarHandler.ProgressType.WorldMesh), chunks.Count + emptyChunkCount);
            for (var i = 0; i < emptyChunkCount; i++)
            {
                loading?.Tick();
            }

            if (chunks.Count == 0)
            {
                return;
            }

            var materials = GatherMaterialEntries(chunks);

            // Heavy ZenKit data extraction incl. vertex deduplication runs on a background thread.
            // The cached world mesh is only read here, which is thread-safe.
            var lightMappedCells = new HashSet<long>();
            var sectorCells = new Dictionary<long, int>();
            var floorCells = new Dictionary<long, int>();
            var materialSectors = _roomService.GetMaterialSectors(_mesh);
            var lightmaps = PrepareLightmaps();
            var lightmapCells = new Dictionary<long, float4>();
            var buffers = await Task.Run(() =>
                ExtractChunks(chunks, materials, lightMappedCells, sectorCells, floorCells, materialSectors, lightmaps,
                    lightmapCells));
            // Mostly light mapped (the Old Mine: 92%): VOBs far from any lightmap get the world's average light.
            _stationaryLightsService.SetLightmapCells(lightmapCells,
                _lightmappedPolygonCount > _extractedPolygonCount / 2);
            _stationaryLightsService.SetLightMappedCells(lightMappedCells);
            _roomService.SetSectorCells(sectorCells, floorCells);
            try
            {
                var meshes = await BuildChunkMeshes(chunks.Count, buffers);
                await CreateChunkGameObjects(chunks, buffers, meshes, loading);
            }
            finally
            {
                buffers.Dispose();
            }
        }

        private List<ChunkBuildData> CollectChunks(out int emptyChunkCount)
        {
            var chunks = new List<ChunkBuildData>();
            var emptyCount = 0;

            void Collect(List<WorldChunkCacheCreatorDomain.WorldChunk> typeChunks, TextureCacheService.TextureArrayTypes type)
            {
                foreach (var chunk in typeChunks)
                {
                    if (chunk.PolygonIds.Count == 0)
                    {
                        emptyCount++;
                    }
                    else
                    {
                        chunks.Add(new ChunkBuildData { Type = type, Chunk = chunk });
                    }
                }
            }

            Collect(_worldChunks.OpaqueChunks, TextureCacheService.TextureArrayTypes.Opaque);
            Collect(_worldChunks.TransparentChunks, TextureCacheService.TextureArrayTypes.Transparent);
            Collect(_worldChunks.WaterChunks, TextureCacheService.TextureArrayTypes.Water);

            emptyChunkCount = emptyCount;
            return chunks;
        }

        /// <summary>
        /// The chunk's polygons without noCollDet materials, fan triangulated like the render mesh.
        /// </summary>
        [CanBeNull]
        private Mesh BuildChunkCollisionMesh(ChunkBuildData data)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var vertexByPosition = new Dictionary<int, int>();
            foreach (var polygonId in data.Chunk.PolygonIds)
            {
                var polygon = _mesh.GetPolygon(polygonId);
                if (_noCollisionMaterials[polygon.MaterialIndex])
                    continue;

                var positionIndices = polygon.PositionIndices;
                int Corner(int fanIndex)
                {
                    var positionIndex = positionIndices[fanIndex];
                    if (vertexByPosition.TryGetValue(positionIndex, out var vertex))
                        return vertex;
                    var position = _mesh.GetPosition(positionIndex);
                    vertex = vertices.Count;
                    vertices.Add(new Vector3(position.X, position.Y, position.Z) / 100f);
                    vertexByPosition[positionIndex] = vertex;
                    return vertex;
                }

                for (var p = 1; p < positionIndices.Count - 1; p++)
                {
                    triangles.Add(Corner(0));
                    triangles.Add(Corner(p + 1));
                    triangles.Add(Corner(p));
                }
            }

            if (triangles.Count == 0)
                return null;

            var collisionMesh = new Mesh { name = "Collision" };
            if (vertices.Count > ushort.MaxValue)
                collisionMesh.indexFormat = IndexFormat.UInt32;
            collisionMesh.SetVertices(vertices);
            collisionMesh.SetTriangles(triangles, 0);
            return collisionMesh;
        }

        private MaterialEntry[] GatherMaterialEntries(List<ChunkBuildData> chunks)
        {
            var entries = new MaterialEntry[_mesh.MaterialCount];
            var gathered = new bool[_mesh.MaterialCount];
            _noCollisionMaterials = new bool[_mesh.MaterialCount];

            foreach (var data in chunks)
            {
                var expandedVertexCount = 0;
                foreach (var polygonId in data.Chunk.PolygonIds)
                {
                    var polygon = _mesh.GetPolygon(polygonId);
                    expandedVertexCount += Math.Max(0, (polygon.PositionIndices.Count - 2) * 3);

                    var materialIndex = polygon.MaterialIndex;
                    if (gathered[materialIndex])
                    {
                        continue;
                    }
                    gathered[materialIndex] = true;

                    var material = _mesh.GetMaterial(materialIndex);
                    _noCollisionMaterials[materialIndex] = material.DisableCollision;

                    var textureArrayIndex = -1;
                    var textureScale = Vector2.one;
                    var maxMipLevel = 0;
                    var animFrameCount = 0;

                    if (UseTextureArray)
                    {
                        TextureCacheService.GetTextureArrayIndex(material, out _, out textureArrayIndex,
                            out textureScale, out maxMipLevel, out animFrameCount);
                    }

                    var animDirection = material.TextureAnimationMapping == AnimationMapping.Linear
                        ? material.TextureAnimationMappingDirection.ToUnityVector()
                        : Vector2.zero;

                    entries[materialIndex] = new MaterialEntry
                    {
                        TextureArrayIndex = textureArrayIndex,
                        TextureScale = new float2(textureScale.x, textureScale.y),
                        MaxMipLevel = maxMipLevel,
                        TexAnim = new float4(animDirection.x, animDirection.y, animFrameCount + 1,
                            material.TextureAnimationFps)
                    };
                }

                data.ExpandedVertexCount = expandedVertexCount;
            }

            return entries;
        }

        /// <summary>
        /// Marks the RoomService cells an indoor polygon covers with its sector (bounding box, capped for huge ones).
        /// Level polygons (floors) also go into the finer floor cells - a room is the floor under the feet.
        /// </summary>
        private void AddSectorCells(int sector, IList<int> positionIndices, Dictionary<long, int> sectorCells,
            Dictionary<long, int> floorCells)
        {
            if (positionIndices.Count == 0)
                return;

            if (positionIndices.Count >= 3 && IsLevel(positionIndices))
                AddFloorCells(sector, positionIndices, floorCells);

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var index in positionIndices)
            {
                var p = _mesh.GetPosition(index);
                var position = new Vector3(p.X, p.Y, p.Z) / 100f;
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            const float cell = RoomService.CellSize;
            const int maxCellsPerAxis = 12;
            var steps = Vector3Int.Min(Vector3Int.CeilToInt((max - min) / cell), Vector3Int.one * maxCellsPerAxis);
            for (var x = 0; x <= steps.x; x++)
                for (var y = 0; y <= steps.y; y++)
                    for (var z = 0; z <= steps.z; z++)
                        sectorCells[RoomService.ToCell(min + new Vector3(x, y, z) * cell)] = sector;
        }

        /// <summary>
        /// Floors (and ceilings, never at the feet): the polygon's normal points (nearly) up or down.
        /// </summary>
        private bool IsLevel(IList<int> positionIndices)
        {
            var a = _mesh.GetPosition(positionIndices[0]);
            var b = _mesh.GetPosition(positionIndices[1]);
            var c = _mesh.GetPosition(positionIndices[2]);
            var normal = Vector3.Cross(new Vector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z),
                new Vector3(c.X - a.X, c.Y - a.Y, c.Z - a.Z));
            return normal.sqrMagnitude > 0f && Mathf.Abs(normal.normalized.y) > _floorMinNormalY;
        }

        private void AddFloorCells(int sector, IList<int> positionIndices, Dictionary<long, int> floorCells)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var index in positionIndices)
            {
                var p = _mesh.GetPosition(index);
                var position = new Vector3(p.X, p.Y, p.Z) / 100f;
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            const float cell = RoomService.FloorCellSize;
            const int maxCellsPerAxis = 40;
            var steps = Vector3Int.Min(Vector3Int.CeilToInt((max - min) / cell), Vector3Int.one * maxCellsPerAxis);
            for (var x = 0; x <= steps.x; x++)
                for (var y = 0; y <= steps.y; y++)
                    for (var z = 0; z <= steps.z; z++)
                        floorCells[RoomService.ToFloorCell(min + Vector3.Min(new Vector3(x, y, z) * cell, max - min))] =
                            sector;
        }

        /// <summary>
        /// DeveloperConfig.EnableWorldLightmaps: Gothic lit the light mapped polygons (caves, mines, houses - 92% of
        /// the Old Mine) with lightmaps compiled from the static lights; their vertex light is dim or black. The
        /// world's lightmap pages (34 unique 256x256 R5G6B5 in the Old Mine, 42 in G1's world) go into one texture
        /// array (smaller pages sit in its corner, their uv scaled), Lit-World samples it for those polygons.
        /// Returns null without lightmaps - the shader keeps a 1x1 white array then.
        /// </summary>
        [CanBeNull]
        private LightmapData PrepareLightmaps()
        {
            if (_emptyLightmaps == null)
            {
                _emptyLightmaps = new Texture2DArray(1, 1, 1, UnityEngine.TextureFormat.RGBA32, false) { name = "EmptyLightmaps" };
                _emptyLightmaps.SetPixelData(new byte[] { 255, 255, 255, 255 }, 0, 0);
                _emptyLightmaps.Apply(false, true);
            }
            if (_worldLightmaps != null)
            {
                UnityEngine.Object.Destroy(_worldLightmaps);
                _worldLightmaps = null;
            }
            Shader.SetGlobalTexture(_worldLightmapsShaderId, _emptyLightmaps);
            Shader.SetGlobalFloat(_worldLightmapBrightnessShaderId, _worldConfigService.Dev.WorldLightmapBrightness);
            _stationaryLightsService.HasWorldLightmaps = false;

            var count = _mesh.LightMapCount;
            if (!_worldConfigService.Dev.EnableWorldLightmaps || count == 0)
                return null;

            var data = new LightmapData
            {
                Origin = new float3[count],
                U = new float3[count],
                V = new float3[count],
                Scale = new float2[count],
                Slice = new int[count]
            };

            // Lightmap entries share pages (the Old Mine: 790 entries on 34 pages) - one slice per unique page.
            var pages = new List<byte[]>();
            var pageByHash = new Dictionary<string, int>();
            using var sha = System.Security.Cryptography.SHA1.Create();
            for (var i = 0; i < count; i++)
            {
                var lightmap = _mesh.GetLightMap(i);
                var image = lightmap.Image;
                var width = Mathf.Min(image.Width, _lightmapPageSize);
                var height = Mathf.Min(image.Height, _lightmapPageSize);
                var rgba = GetLightmapRgba(image);

                var hash = rgba == null ? string.Empty : Convert.ToBase64String(sha.ComputeHash(rgba));
                if (rgba != null && !pageByHash.TryGetValue(hash, out data.Slice[i]))
                {
                    data.Slice[i] = pages.Count;
                    pageByHash[hash] = pages.Count;
                    pages.Add(ToPage(rgba, image.Width, width, height));
                }

                data.Origin[i] = new float3(lightmap.Origin.X, lightmap.Origin.Y, lightmap.Origin.Z);
                // ZenGin stores the lightmap's up vector first, then its right vector: u = right, v = up. Checked
                // against G1's world and Old Mine - the other way round sampled mostly black atlas gaps.
                data.U[i] = new float3(lightmap.Normals.Item2.X, lightmap.Normals.Item2.Y, lightmap.Normals.Item2.Z);
                data.V[i] = new float3(lightmap.Normals.Item1.X, lightmap.Normals.Item1.Y, lightmap.Normals.Item1.Z);
                data.Scale[i] = new float2((float)width / _lightmapPageSize, (float)height / _lightmapPageSize);
            }

            if (pages.Count == 0)
                return null;

            _worldLightmaps = new Texture2DArray(_lightmapPageSize, _lightmapPageSize, pages.Count, UnityEngine.TextureFormat.RGBA32,
                true, false)
            {
                name = "WorldLightmaps",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (var slice = 0; slice < pages.Count; slice++)
                _worldLightmaps.SetPixelData(pages[slice], 0, slice);
            _worldLightmaps.Apply(true, true);

            Shader.SetGlobalTexture(_worldLightmapsShaderId, _worldLightmaps);
            _stationaryLightsService.HasWorldLightmaps = true;
            data.Pages = pages;
            Logger.Log($"[Lightmaps] {count} lightmaps on {pages.Count} pages", LogCat.Mesh);
            return data;
        }

        /// <summary>
        /// The page as RGBA bytes. ZenKit's CachedTexture.GetMipmapRgba returns the raw data - lightmaps are R5G6B5,
        /// decoded here when there are no RGBA mipmaps.
        /// </summary>
        [CanBeNull]
        private static byte[] GetLightmapRgba(ITexture image)
        {
            var rgbaMips = image.AllMipmapsRgba;
            if (rgbaMips != null && rgbaMips.Count > 0 && rgbaMips[0].Length == image.Width * image.Height * 4)
                return rgbaMips[0];

            if (image.Format != ZenKit.TextureFormat.R5G6B5)
            {
                Logger.LogWarning($"[Lightmaps] Unsupported lightmap format {image.Format}", LogCat.Mesh);
                return null;
            }

            var raw = image.GetMipmapRaw(0);
            var pixels = image.Width * image.Height;
            if (raw == null || raw.Length < pixels * 2)
                return null;
            var rgba = new byte[pixels * 4];
            for (var p = 0; p < pixels; p++)
            {
                var value = raw[p * 2] | (raw[p * 2 + 1] << 8);
                rgba[p * 4] = (byte)(((value >> 11) & 0x1F) * 255 / 31);
                rgba[p * 4 + 1] = (byte)(((value >> 5) & 0x3F) * 255 / 63);
                rgba[p * 4 + 2] = (byte)((value & 0x1F) * 255 / 31);
                rgba[p * 4 + 3] = 255;
            }
            return rgba;
        }

        /// <summary>
        /// A 256x256 page with the lightmap in its corner (rows kept like every other texture: the world's uvs and
        /// the texture data follow the same convention).
        /// </summary>
        private static byte[] ToPage(byte[] rgba, int sourceWidth, int width, int height)
        {
            if (width == _lightmapPageSize && height == _lightmapPageSize && sourceWidth == _lightmapPageSize)
                return rgba;

            var page = new byte[_lightmapPageSize * _lightmapPageSize * 4];
            for (var y = 0; y < height; y++)
                Buffer.BlockCopy(rgba, y * sourceWidth * 4, page, y * _lightmapPageSize * 4, width * 4);
            return page;
        }

        /// <summary>
        /// DeveloperConfig.EnableVobWorldLight: the polygon's lightmap light at its center, summed into its cell - VOBs
        /// in light mapped areas take it (StationaryLightsService.ApplyWorldLight).
        /// </summary>
        private void AddLightmapCell(IPolygon polygon, IReadOnlyList<int> positionIndices, [CanBeNull] LightmapData lightmaps,
            Dictionary<long, float4> lightmapCells)
        {
            var index = polygon.LightMapIndex;
            if (lightmaps?.Pages == null || index >= lightmaps.Slice.Length)
                return;
            var slice = lightmaps.Slice[index];
            if (slice < 0 || slice >= lightmaps.Pages.Count)
                return;

            var center = float3.zero;
            foreach (var positionIndex in positionIndices)
            {
                var position = _mesh.GetPosition(positionIndex);
                center += new float3(position.X, position.Y, position.Z);
            }
            center /= positionIndices.Count;

            var offset = center - lightmaps.Origin[index];
            var u = math.dot(offset, lightmaps.U[index]) * lightmaps.Scale[index].x;
            var v = math.dot(offset, lightmaps.V[index]) * lightmaps.Scale[index].y;
            var x = math.clamp((int)(u * _lightmapPageSize), 0, _lightmapPageSize - 1);
            var y = math.clamp((int)(v * _lightmapPageSize), 0, _lightmapPageSize - 1);
            var page = lightmaps.Pages[slice];
            var p = (y * _lightmapPageSize + x) * 4;
            var light = new float4(page[p] / 255f, page[p + 1] / 255f, page[p + 2] / 255f, 1f);

            var cell = StationaryLightsService.ToCell(new Vector3(center.x, center.y, center.z) / 100f);
            lightmapCells[cell] = lightmapCells.TryGetValue(cell, out var sum) ? sum + light : light;
        }

        private ExtractionBuffers ExtractChunks(List<ChunkBuildData> chunks, MaterialEntry[] materials,
            HashSet<long> lightMappedCells, Dictionary<long, int> sectorCells, Dictionary<long, int> floorCells,
            int[] materialSectors, [CanBeNull] LightmapData lightmaps, Dictionary<long, float4> lightmapCells)
        {
            // Pass 1 - size the buffers from the expanded vertex counts cached by GatherMaterialEntries
            // (worst case, pre-deduplication). No second GetPolygon sweep needed here.
            var worldVertexCapacity = 0;
            var waterVertexCapacity = 0;
            var indexCapacity = 0;
            foreach (var data in chunks)
            {
                var expanded = data.ExpandedVertexCount;

                if (data.Type == TextureCacheService.TextureArrayTypes.Water)
                {
                    waterVertexCapacity += expanded;
                }
                else
                {
                    worldVertexCapacity += expanded;
                }
                indexCapacity += expanded;
            }

            var buffers = new ExtractionBuffers
            {
                Ranges = new NativeArray<WorldChunkMeshRange>(chunks.Count, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                Vertices = new NativeArray<WorldChunkVertex>(Math.Max(1, worldVertexCapacity), Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                WaterVertices = new NativeArray<WorldWaterVertex>(Math.Max(1, waterVertexCapacity), Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                Indices = new NativeArray<int>(Math.Max(1, indexCapacity), Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory)
            };

            // Pass 2 - flatten the polygons into per-chunk vertex/index ranges with deduplication.
            // Vertices are unique per position + feature + material combination.
            var worldVertexCursor = 0;
            var waterVertexCursor = 0;
            var indexCursor = 0;
            var deduplication = new Dictionary<(int Position, int Feature, int Material, int LightMap), int>();

            for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                var data = chunks[chunkIndex];
                var isWater = data.Type == TextureCacheService.TextureArrayTypes.Water;
                var vertexStart = isWater ? waterVertexCursor : worldVertexCursor;
                var indexStart = indexCursor;
                var vertexCount = 0;
                var boundsMin = new float3(float.MaxValue);
                var boundsMax = new float3(float.MinValue);
                deduplication.Clear();

                foreach (var polygonId in data.Chunk.PolygonIds)
                {
                    var polygon = _mesh.GetPolygon(polygonId);
                    var positionIndices = polygon.PositionIndices;
                    _extractedPolygonCount++;
                    if (polygon.LightMapIndex >= 0)
                        _lightmappedPolygonCount++;
                    var featureIndices = polygon.FeatureIndices;
                    var materialIndex = polygon.MaterialIndex;
                    var material = materials[materialIndex];
                    if (_noCollisionMaterials[materialIndex])
                        data.HasNoCollisionPolygons = true;

                    // Gothic lit these (caves, houses) with lightmaps from static lights, which we don't render.
                    // Their static lights are shown as runtime stationary lights instead (StationaryLightsService).
                    if (polygon.LightMapIndex >= 0 && positionIndices.Count > 0)
                    {
                        var corner = _mesh.GetPosition(positionIndices[0]);
                        lightMappedCells.Add(StationaryLightsService.ToCell(
                            new Vector3(corner.X, corner.Y, corner.Z) / 100f));
                        AddLightmapCell(polygon, positionIndices, lightmaps, lightmapCells);
                    }

                    // Portal rooms (huts, houses): like OpenGothic, a room's polygons carry its sector in their
                    // material name (RoomService.GetMaterialSectors).
                    if (materialIndex < materialSectors.Length && materialSectors[materialIndex] >= 0)
                        AddSectorCells(materialSectors[materialIndex], positionIndices, sectorCells, floorCells);

                    var lightMapIndex = lightmaps != null && polygon.LightMapIndex >= 0 &&
                                        polygon.LightMapIndex < lightmaps.Slice.Length
                        ? polygon.LightMapIndex
                        : -1;

                    void AddCorner(int fanIndex)
                    {
                        var key = (positionIndices[fanIndex], featureIndices[fanIndex], materialIndex, lightMapIndex);
                        if (!deduplication.TryGetValue(key, out var localIndex))
                        {
                            localIndex = vertexCount++;
                            deduplication[key] = localIndex;

                            var position = _mesh.GetPosition(key.Item1);
                            var feature = _mesh.GetFeature(key.Item2);
                            var unityPosition = new float3(position.X, position.Y, position.Z) / 100f;
                            boundsMin = math.min(boundsMin, unityPosition);
                            boundsMax = math.max(boundsMax, unityPosition);

                            var uv = new float4(
                                feature.Texture.X * material.TextureScale.x,
                                feature.Texture.Y * material.TextureScale.y,
                                material.TextureArrayIndex,
                                material.MaxMipLevel);

                            if (isWater)
                            {
                                buffers.WaterVertices[vertexStart + localIndex] = new WorldWaterVertex
                                {
                                    Position = unityPosition,
                                    Uv = uv,
                                    TexAnim = material.TexAnim
                                };
                            }
                            else
                            {
                                var lightmapUv = float4.zero;
                                if (lightMapIndex >= 0)
                                {
                                    var offset = new float3(position.X, position.Y, position.Z) -
                                                 lightmaps.Origin[lightMapIndex];
                                    lightmapUv = new float4(
                                        math.dot(offset, lightmaps.U[lightMapIndex]) * lightmaps.Scale[lightMapIndex].x,
                                        math.dot(offset, lightmaps.V[lightMapIndex]) * lightmaps.Scale[lightMapIndex].y,
                                        lightmaps.Slice[lightMapIndex],
                                        -1f);
                                }

                                var light = (uint)feature.Light;
                                buffers.Vertices[vertexStart + localIndex] = new WorldChunkVertex
                                {
                                    Position = unityPosition,
                                    NormalX = ToSnorm16(feature.Normal.X),
                                    NormalY = ToSnorm16(feature.Normal.Y),
                                    NormalZ = ToSnorm16(feature.Normal.Z),
                                    // Outdoor (vertex lit) polygons follow the time of day (Lit-World
                                    // _DayLightColor), light mapped rooms and caves keep their light. G1's
                                    // polygons have no outdoor flag set.
                                    NormalW = polygon.LightMapIndex < 0 ? short.MaxValue : (short)0,
                                    // Gothic's baked vertex light, kept untouched: ARGB int -> RGBA byte order.
                                    Color = ((light >> 16) & 0xFF) | (((light >> 8) & 0xFF) << 8) |
                                            ((light & 0xFF) << 16) | (((light >> 24) & 0xFF) << 24),
                                    Uv = uv,
                                    LightmapUv = lightmapUv
                                };
                            }
                        }

                        buffers.Indices[indexCursor++] = localIndex;
                    }

                    for (var p = 1; p < positionIndices.Count - 1; p++)
                    {
                        // Fan triangulation. Second and third corner are swapped to flip the
                        // triangle winding for Unity's coordinate system (Gothic -> Unity fix).
                        AddCorner(0);
                        AddCorner(p + 1);
                        AddCorner(p);
                    }
                }

                if (isWater)
                {
                    waterVertexCursor += vertexCount;
                }
                else
                {
                    worldVertexCursor += vertexCount;
                }

                buffers.Ranges[chunkIndex] = new WorldChunkMeshRange
                {
                    VertexStart = vertexStart,
                    VertexCount = vertexCount,
                    IndexStart = indexStart,
                    IndexCount = indexCursor - indexStart,
                    IsWater = isWater,
                    Use16BitIndices = vertexCount <= ushort.MaxValue,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax
                };
            }

            return buffers;
        }

        private async Task<Mesh[]> BuildChunkMeshes(int chunkCount, ExtractionBuffers buffers)
        {
            var meshDataArray = Mesh.AllocateWritableMeshData(chunkCount);
            var jobHandle = default(JobHandle);
            try
            {
                for (var i = 0; i < chunkCount; i++)
                {
                    var range = buffers.Ranges[i];
                    var meshData = meshDataArray[i];
                    meshData.SetVertexBufferParams(range.VertexCount, range.IsWater ? _waterVertexLayout : _worldVertexLayout);
                    meshData.SetIndexBufferParams(range.IndexCount, range.Use16BitIndices ? IndexFormat.UInt16 : IndexFormat.UInt32);
                }

                var writeJob = new WorldMeshWriteJob
                {
                    Ranges = buffers.Ranges,
                    Vertices = buffers.Vertices,
                    WaterVertices = buffers.WaterVertices,
                    Indices = buffers.Indices,
                    MeshData = meshDataArray
                };
                jobHandle = writeJob.Schedule(chunkCount, 1);
                JobHandle.ScheduleBatchedJobs();

                // Let the Burst workers fill the mesh buffers without blocking the main thread.
                while (!jobHandle.IsCompleted)
                {
                    await Task.Yield();
                }
                jobHandle.Complete();
            }
            catch
            {
                // The scheduled job still owns meshDataArray; finish it before disposing the native memory,
                // otherwise (e.g. on a cancellation thrown inside the await loop) we'd dispose buffers in use.
                jobHandle.Complete();
                meshDataArray.Dispose();
                throw;
            }

            var meshes = new Mesh[chunkCount];
            for (var i = 0; i < chunkCount; i++)
            {
                meshes[i] = new Mesh();
            }

            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, meshes,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);

            return meshes;
        }

        private async Task CreateChunkGameObjects(List<ChunkBuildData> chunks, ExtractionBuffers buffers, Mesh[] meshes,
            [CanBeNull] LoadingService loading)
        {
            var typeRoots = new Dictionary<TextureCacheService.TextureArrayTypes, GameObject>();
            var typeCounters = new Dictionary<TextureCacheService.TextureArrayTypes, int>();
            foreach (var type in new[]
                     {
                         TextureCacheService.TextureArrayTypes.Opaque,
                         TextureCacheService.TextureArrayTypes.Transparent,
                         TextureCacheService.TextureArrayTypes.Water
                     })
            {
                var typeRoot = new GameObject
                {
                    name = type.ToString(),
                    isStatic = true
                };
                typeRoot.SetParent(RootGo);
                typeRoots[type] = typeRoot;
                typeCounters[type] = 0;
            }

            for (var i = 0; i < chunks.Count; i++)
            {
                await _frameSkipperService.TrySkipToNextFrame();

                var data = chunks[i];
                var range = buffers.Ranges[i];
                var mesh = meshes[i];

                var chunkGo = new GameObject
                {
                    name = $"{data.Type}-Entry-{typeCounters[data.Type]++}",
                    isStatic = true,
                    layer = data.Type == TextureCacheService.TextureArrayTypes.Water
                        ? Constants.WaterLayer
                        : Constants.DefaultLayer
                };
                chunkGo.SetParent(typeRoots[data.Type]);

                mesh.name = chunkGo.name;
                var bounds = new Bounds();
                bounds.SetMinMax(range.BoundsMin, range.BoundsMax);
                mesh.bounds = bounds;

                var meshFilter = chunkGo.AddComponent<MeshFilter>();
                var meshRenderer = chunkGo.AddComponent<MeshRenderer>();
                meshFilter.sharedMesh = mesh;
                _stationaryLightsService.RegisterWorldChunk(meshRenderer);

                PrepareMeshRenderer(meshRenderer, data.Type);
                // DeveloperConfig.EnableMaterialCollisionFilter: like the engine, noCollDet polygons (bushes, leaves on
                // the level mesh) don't collide - they blocked the player before.
                if (_worldConfigService.Dev.EnableMaterialCollisionFilter && data.HasNoCollisionPolygons &&
                    data.Type != TextureCacheService.TextureArrayTypes.Water)
                {
                    var collisionMesh = BuildChunkCollisionMesh(data);
                    if (collisionMesh != null)
                        PrepareMeshCollider(chunkGo, collisionMesh);
                }
                else
                {
                    PrepareMeshCollider(chunkGo, mesh);
                }

#if UNITY_EDITOR
                if (data.Type != TextureCacheService.TextureArrayTypes.Opaque)
                {
                    UnityEditor.GameObjectUtility.SetStaticEditorFlags(chunkGo,
                        (UnityEditor.StaticEditorFlags)(int.MaxValue & ~(int)UnityEditor.StaticEditorFlags.OccluderStatic));
                }
#endif

                loading?.Tick();
            }
        }

        private void PrepareMeshRenderer(Renderer rend, TextureCacheService.TextureArrayTypes textureArrayType)
        {
            if (UseTextureArray)
            {
                var material = GetDefaultMaterial(textureArrayType);
                rend.material = material;
                material.mainTexture = TextureCacheService.GetTextureArrayEntry(textureArrayType);
            }
            else
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                rend.material = material;
                material.mainTexture = Constants.TextureGothicUnityLogoInverse;
            }
        }

        private static Material GetDefaultMaterial(TextureCacheService.TextureArrayTypes textureArrayType)
        {
            var shader = textureArrayType switch
            {
                TextureCacheService.TextureArrayTypes.Opaque => Constants.ShaderWorldLit,
                TextureCacheService.TextureArrayTypes.Transparent => Constants.ShaderLitAlphaToCoverage,
                TextureCacheService.TextureArrayTypes.Water => Constants.ShaderWater,
                _ => throw new ArgumentOutOfRangeException(nameof(textureArrayType), textureArrayType, null)
            };

            // Render queues are defined by the shaders' "Queue" tags.
            return new Material(shader);
        }

        private static short ToSnorm16(float value)
        {
            return (short)math.round(math.clamp(value, -1f, 1f) * short.MaxValue);
        }
    }
}
