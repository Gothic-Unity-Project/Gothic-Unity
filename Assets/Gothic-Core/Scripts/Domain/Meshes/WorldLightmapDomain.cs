using System;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using JetBrains.Annotations;
using Unity.Mathematics;
using UnityEngine;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Meshes
{
    /// <summary>
    /// DeveloperConfig.EnableWorldLightmaps: Gothic lit the light mapped polygons (caves, mines, houses - 92% of
    /// the Old Mine) with lightmaps compiled from the static lights; their vertex light is dim or black. The
    /// world's lightmap pages (34 unique 256x256 R5G6B5 in the Old Mine, 42 in G1's world) become RGBA pages of one
    /// texture array (smaller pages sit in its corner, their uv scaled). StationaryLightsService owns that array,
    /// WorldMeshBuilder writes the per-vertex lightmap uv.
    /// </summary>
    public class WorldLightmapDomain
    {
        public const int PageSize = 256;

        /// <summary>
        /// Per lightmap of the world mesh, how a vertex position (Gothic cm) becomes its lightmap uv (ZenGin
        /// zCLightMap: uv = dot(position - origin, U/V), the vectors are scaled to the page) and which slice of the
        /// lightmap array holds its page.
        /// </summary>
        public sealed class LightmapData
        {
            public float3[] Origin;
            public float3[] U;
            public float3[] V;
            public float2[] Scale;
            public int[] Slice;
            public List<byte[]> Pages;

            public int Count => Slice.Length;

            public bool Contains(int lightMapIndex) => lightMapIndex >= 0 && lightMapIndex < Slice.Length;

            /// <summary>
            /// The lightmap uv of a vertex (Gothic cm): uv, array slice, -1 = light mapped (Lit-World samples
            /// _WorldLightmaps).
            /// </summary>
            public float4 GetVertexUv(int lightMapIndex, float3 position)
            {
                var offset = position - Origin[lightMapIndex];
                return new float4(
                    math.dot(offset, U[lightMapIndex]) * Scale[lightMapIndex].x,
                    math.dot(offset, V[lightMapIndex]) * Scale[lightMapIndex].y,
                    Slice[lightMapIndex],
                    -1f);
            }

            /// <summary>
            /// The lightmap light (rgb, w = 1) at a position (Gothic cm) of a polygon with this lightmap.
            /// </summary>
            public bool TrySample(int lightMapIndex, float3 position, out float4 light)
            {
                light = default;
                if (Pages == null || !Contains(lightMapIndex))
                    return false;
                var slice = Slice[lightMapIndex];
                if (slice < 0 || slice >= Pages.Count)
                    return false;

                var offset = position - Origin[lightMapIndex];
                var u = math.dot(offset, U[lightMapIndex]) * Scale[lightMapIndex].x;
                var v = math.dot(offset, V[lightMapIndex]) * Scale[lightMapIndex].y;
                var x = math.clamp((int)(u * PageSize), 0, PageSize - 1);
                var y = math.clamp((int)(v * PageSize), 0, PageSize - 1);
                var page = Pages[slice];
                var p = (y * PageSize + x) * 4;
                light = new float4(page[p] / 255f, page[p + 1] / 255f, page[p + 2] / 255f, 1f);
                return true;
            }
        }

        /// <summary>
        /// The mesh's lightmaps with their unique pages, null without lightmaps (or none decodable).
        /// </summary>
        [CanBeNull]
        public LightmapData PrepareLightmaps(IMesh mesh)
        {
            var count = mesh.LightMapCount;
            if (count == 0)
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
                var lightmap = mesh.GetLightMap(i);
                var image = lightmap.Image;
                var width = Mathf.Min(image.Width, PageSize);
                var height = Mathf.Min(image.Height, PageSize);
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
                data.Scale[i] = new float2((float)width / PageSize, (float)height / PageSize);
            }

            if (pages.Count == 0)
                return null;

            data.Pages = pages;
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

            return image.GetR5G6B5MipmapRgba(0);
        }

        /// <summary>
        /// A 256x256 page with the lightmap in its corner (rows kept like every other texture: the world's uvs and
        /// the texture data follow the same convention).
        /// </summary>
        private static byte[] ToPage(byte[] rgba, int sourceWidth, int width, int height)
        {
            if (width == PageSize && height == PageSize && sourceWidth == PageSize)
                return rgba;

            var page = new byte[PageSize * PageSize * 4];
            for (var y = 0; y < height; y++)
                Buffer.BlockCopy(rgba, y * sourceWidth * 4, page, y * PageSize * 4, width * 4);
            return page;
        }
    }
}
