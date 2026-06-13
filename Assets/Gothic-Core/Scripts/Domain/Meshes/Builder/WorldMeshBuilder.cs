using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gothic.Core.Adapters.UI.LoadingBars;
using Gothic.Core.Domain.StaticCache;
using Gothic.Core.Extensions;
using Gothic.Core.Const;
using Gothic.Core.Manager;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.StaticCache;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using ZenKit;
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

        private StaticCacheService.WorldChunkContainer _worldChunks;
        private IMesh _mesh;

        private static readonly VertexAttributeDescriptor[] _worldVertexLayout =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.Normal, VertexAttributeFormat.SNorm16, 4),
            new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4)
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
        }

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
            var buffers = await Task.Run(() => ExtractChunks(chunks, materials));
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

        private MaterialEntry[] GatherMaterialEntries(List<ChunkBuildData> chunks)
        {
            var entries = new MaterialEntry[_mesh.MaterialCount];
            var gathered = new bool[_mesh.MaterialCount];

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

        private ExtractionBuffers ExtractChunks(List<ChunkBuildData> chunks, MaterialEntry[] materials)
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
            var deduplication = new Dictionary<(int Position, int Feature, int Material), int>();

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
                    var featureIndices = polygon.FeatureIndices;
                    var materialIndex = polygon.MaterialIndex;
                    var material = materials[materialIndex];

                    void AddCorner(int fanIndex)
                    {
                        var key = (positionIndices[fanIndex], featureIndices[fanIndex], materialIndex);
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
                                var light = (uint)feature.Light;
                                buffers.Vertices[vertexStart + localIndex] = new WorldChunkVertex
                                {
                                    Position = unityPosition,
                                    NormalX = ToSnorm16(feature.Normal.X),
                                    NormalY = ToSnorm16(feature.Normal.Y),
                                    NormalZ = ToSnorm16(feature.Normal.Z),
                                    NormalW = 0,
                                    // Gothic's baked vertex light, kept untouched: ARGB int -> RGBA byte order.
                                    Color = ((light >> 16) & 0xFF) | (((light >> 8) & 0xFF) << 8) |
                                            ((light & 0xFF) << 16) | (((light >> 24) & 0xFF) << 24),
                                    Uv = uv
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

                PrepareMeshRenderer(meshRenderer, data.Type);
                PrepareMeshCollider(chunkGo, mesh);

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
