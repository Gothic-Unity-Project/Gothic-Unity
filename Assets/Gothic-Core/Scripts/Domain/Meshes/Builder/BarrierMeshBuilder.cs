using System;
using System.Collections.Generic;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Models.Container;
using UnityEngine;
using ZenKit;
using Logger = GUZ.Core.Logging.Logger;
using Material = UnityEngine.Material;
using Mesh = UnityEngine.Mesh;
using Vector3 = System.Numerics.Vector3;

namespace GUZ.Core.Domain.Meshes.Builder
{
    public class BarrierMeshBuilder : AbstractMeshBuilder
    {
        private const string _barrierTextureName = "Barriere";
        private IMesh _barrierMesh;

        public void SetBarrierMesh(IMesh mesh)
        {
            _barrierMesh = mesh;
        }

        public override GameObject Build()
        {
            var meshColors = new List<Color>();

            var maxSkyY = _barrierMesh.BoundingBox.Max.Y; // Assuming AxisAlignedBoundingBox has Min and Max as Vector3
            var minSkyY = maxSkyY * 0.925f;

            var subMeshesData = new Dictionary<int, WorldContainer.SubMeshData>();
            for (var i = 0; i < _barrierMesh.MaterialCount; i++)
            {
                subMeshesData[i] = new WorldContainer.SubMeshData { Material = _barrierMesh.Materials[i] };
            }

            foreach (var polygon in _barrierMesh.Polygons)
            {
                var submesh = subMeshesData[polygon.MaterialIndex];
                // As we always use element 0 and i+1, we skip it in the loop.
                for (var i = 1; i < polygon.PositionIndices.Count - 1; i++)
                {
                    var vertFeature = _barrierMesh.GetPosition(polygon.PositionIndices[i]);

                    var vertY = vertFeature.Y;
                    int alpha;

                    if (vertY > minSkyY)
                    {
                        alpha = (int)(255.0f * (maxSkyY - vertY) / (maxSkyY - minSkyY));
                    }
                    else
                    {
                        alpha = (int)(255.0f * (vertY / 8000.0f));
                    }

                    alpha = Math.Clamp(alpha, 0, 255);

                    // Triangle Fan - We need to add element 0 (A) before every triangle 2 elements.
                    AddEntry(_barrierMesh.Positions, _barrierMesh.Features, polygon, meshColors, alpha, submesh, 0);
                    AddEntry(_barrierMesh.Positions, _barrierMesh.Features, polygon, meshColors, alpha, submesh, i);
                    AddEntry(_barrierMesh.Positions, _barrierMesh.Features, polygon, meshColors, alpha, submesh, i + 1);
                }
            }

            // To have easier to read code above, we reverse the arrays now at the end.
            foreach (var subMesh in subMeshesData)
            {
                subMesh.Value.Vertices.Reverse();
                subMesh.Value.Uvs.Reverse();
                subMesh.Value.Normals.Reverse();
                meshColors.Reverse();
            }

            foreach (var subMesh in subMeshesData.Values)
            {
                // No texture to add.
                // For G1 this is: material.name == [KEINE, KEINETEXTUREN, DEFAULT, BRETT2, BRETT1, SUMPFWAASER, S:PSIT01_ABODEN]
                // Removing these removes tiny slices of walls on the ground. If anyone finds them, I owe them a beer. ;-)
                if (subMesh.Material.Texture.IsEmpty() || subMesh.Triangles.IsEmpty())
                {
                    continue;
                }

                var subMeshObj = new GameObject
                {
                    name = subMesh.Material.Name,
                    isStatic = true
                };

                subMeshObj.SetParent(RootGo);

                var meshFilter = subMeshObj.AddComponent<MeshFilter>();
                var meshRenderer = subMeshObj.AddComponent<MeshRenderer>();

                PrepareBarrierMeshRenderer(meshRenderer, subMesh);
                PrepareBarrierMeshFilter(meshFilter, subMesh, meshColors.ToArray());
            }

            return RootGo;
        }

        private void PrepareBarrierMeshRenderer(Renderer rend, WorldContainer.SubMeshData subMesh)
        {
            var bMaterial = subMesh.Material;

            var texture = GetTexture(_barrierTextureName);

            if (null == texture)
            {
                if (bMaterial.Texture.EndsWithIgnoreCase(".TGA"))
                {
                    Logger.LogError($"This is supposed to be a decal: ${bMaterial.Texture}", LogCat.Mesh);
                }
                else
                {
                    Logger.LogError($"Couldn't get texture from name: {bMaterial.Texture}", LogCat.Mesh);
                }
            }

            Material material;
            switch (subMesh.Material.Group)
            {
                case MaterialGroup.Water:
                    material = GetWaterMaterial();
                    break;
                default:
                    material = new Material(Constants.ShaderBarrier);
                    break;
            }

            // No texture to add.
            if (bMaterial.Texture.IsEmpty())
            {
                Logger.LogWarning($"No texture was set for: {bMaterial.Name}", LogCat.Mesh);
                return;
            }

            material.mainTexture = texture;

            var material2 = new Material(material);
            //
            material2.SetFloat("_WaveIntensity", 1);

            // rend.material = material;


            rend.materials = new[] { material, material2 };
        }

        private void PrepareBarrierMeshFilter(MeshFilter meshFilter, WorldContainer.SubMeshData subMesh,
            Color[] colors = null)
        {
            var mesh = new Mesh();
            meshFilter.sharedMesh = mesh;

            if (subMesh.Triangles.Count % 3 != 0)
            {
                Logger.LogError("Triangle count is not a multiple of 3", LogCat.Mesh);
            }

            mesh.SetVertices(subMesh.Vertices);
            mesh.SetTriangles(subMesh.Triangles, 0);
            mesh.SetUVs(0, subMesh.Uvs);
            mesh.SetColors(colors);
        }

        private static void AddEntry(List<Vector3> zkPositions, List<Vertex> features, IPolygon polygon,
            List<Color> meshColors, float alpha,
            WorldContainer.SubMeshData currentSubMesh, int index)
        {
            // For every vertexIndex we store a new vertex. (i.e. no reuse of Vector3-vertices for later texture/uv attachment)
            var positionIndex = polygon.PositionIndices[index];
            currentSubMesh.Vertices.Add(zkPositions[positionIndex].ToUnityVector());

            meshColors.Add(new Color(1, 1, 1, alpha / 255f));

            // This triangle (index where Vector 3 lies inside vertices, points to the newly added vertex (Vector3) as we don't reuse vertices.
            currentSubMesh.Triangles.Add(currentSubMesh.Vertices.Count - 1);

            var featureIndex = polygon.FeatureIndices[index];
            var feature = features[featureIndex];
            currentSubMesh.Uvs.Add(feature.Texture.ToUnityVector());
            currentSubMesh.Normals.Add(feature.Normal.ToUnityVector());
        }
    }
}
