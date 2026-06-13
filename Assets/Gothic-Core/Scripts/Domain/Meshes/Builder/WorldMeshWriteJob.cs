using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gothic.Core.Domain.Meshes.Builder
{
    /// <summary>
    /// Writes the extracted and deduplicated world chunk data into writable MeshData buffers.
    /// One Execute() call per chunk mesh, Burst-compiled and running in parallel for all chunks at once.
    /// Vertex and index buffer params must be set by the caller before scheduling.
    /// </summary>
    [BurstCompile]
    public struct WorldMeshWriteJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<WorldChunkMeshRange> Ranges;
        [ReadOnly] public NativeArray<WorldChunkVertex> Vertices;
        [ReadOnly] public NativeArray<WorldWaterVertex> WaterVertices;
        [ReadOnly] public NativeArray<int> Indices;

        public Mesh.MeshDataArray MeshData;

        public void Execute(int chunkIndex)
        {
            var range = Ranges[chunkIndex];
            var meshData = MeshData[chunkIndex];

            if (range.IsWater)
            {
                var vertexBuffer = meshData.GetVertexData<WorldWaterVertex>();
                NativeArray<WorldWaterVertex>.Copy(WaterVertices, range.VertexStart, vertexBuffer, 0, range.VertexCount);
            }
            else
            {
                var vertexBuffer = meshData.GetVertexData<WorldChunkVertex>();
                NativeArray<WorldChunkVertex>.Copy(Vertices, range.VertexStart, vertexBuffer, 0, range.VertexCount);
            }

            if (range.Use16BitIndices)
            {
                var indexBuffer = meshData.GetIndexData<ushort>();
                for (var i = 0; i < range.IndexCount; i++)
                {
                    indexBuffer[i] = (ushort)Indices[range.IndexStart + i];
                }
            }
            else
            {
                var indexBuffer = meshData.GetIndexData<uint>();
                for (var i = 0; i < range.IndexCount; i++)
                {
                    indexBuffer[i] = (uint)Indices[range.IndexStart + i];
                }
            }

            var bounds = new Bounds();
            bounds.SetMinMax(range.BoundsMin, range.BoundsMax);

            meshData.subMeshCount = 1;
            meshData.SetSubMesh(0,
                new SubMeshDescriptor(0, range.IndexCount)
                {
                    vertexCount = range.VertexCount,
                    bounds = bounds
                },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
        }
    }
}
