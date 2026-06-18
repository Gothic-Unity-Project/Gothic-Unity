using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Gothic.Core.Domain.Meshes.Builder
{
    /// <summary>
    /// Interleaved vertex layout for opaque and transparent world chunks.
    /// Must match the VertexAttributeDescriptors inside WorldMeshBuilder:
    /// Position (Float32x3), Normal (SNorm16x4), Color (UNorm8x4), TexCoord0 (Float32x4).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WorldChunkVertex
    {
        public float3 Position;
        public short NormalX;
        public short NormalY;
        public short NormalZ;
        public short NormalW;
        public uint Color;
        public float4 Uv;
    }

    /// <summary>
    /// Interleaved vertex layout for water chunks. The water shader uses no normals or vertex colors:
    /// Position (Float32x3), TexCoord0 (Float32x4), TexCoord1 (Float32x4).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WorldWaterVertex
    {
        public float3 Position;
        public float4 Uv;
        public float4 TexAnim;
    }

    /// <summary>
    /// Slice of the flat world extraction buffers which belongs to one chunk mesh.
    /// </summary>
    public struct WorldChunkMeshRange
    {
        public int VertexStart;
        public int VertexCount;
        public int IndexStart;
        public int IndexCount;
        public bool IsWater;
        public bool Use16BitIndices;
        public float3 BoundsMin;
        public float3 BoundsMax;
    }
}
