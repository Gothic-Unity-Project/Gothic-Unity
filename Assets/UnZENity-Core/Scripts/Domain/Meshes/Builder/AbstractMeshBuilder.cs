using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GUZ.Core.Const;
using GUZ.Core.Domain.StaticCache;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.StaticCache;
using JetBrains.Annotations;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering;
using ZenKit;
using Logger = GUZ.Core.Logging.Logger;
using Material = UnityEngine.Material;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Mesh = UnityEngine.Mesh;
using Texture = UnityEngine.Texture;

namespace GUZ.Core.Domain.Meshes.Builder
{
    public abstract class AbstractMeshBuilder
    {
        [Inject] protected readonly VmCacheService VmCacheService;
        [Inject] protected readonly TextureCacheService TextureCacheService;
        [Inject] private readonly MorphMeshCacheService _morphMeshCacheService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly StaticCacheService _staticCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        protected GameObject RootGo;
        protected GameObject ParentGo;
        protected bool HasMeshCollider = true;
        protected bool UseTextureArray;
        protected bool UseColliderCache;

        protected IMultiResolutionMesh Mrm;
        protected IModelHierarchy Mdh;
        protected IModelMesh Mdm;
        protected IMorphMesh Mmb;

        protected Vector3 RootPosition;
        protected Quaternion RootRotation;
        protected string MeshName;

        protected bool IsMorphMeshMappingAlreadyCached;



        public abstract GameObject Build();


        #region Setter

        public void SetGameObject([CanBeNull] GameObject rootGo, string name = null)
        {
            RootGo = rootGo == null ? new GameObject() : rootGo;

            if (name != null)
            {
                RootGo.name = name;
            }
        }

        public void SetParent([CanBeNull] GameObject parentGo, bool resetPosition = false, bool resetRotation = false)
        {
            if (parentGo == null)
            {
                return;
            }

            ParentGo = parentGo;
            RootGo.SetParent(parentGo, resetPosition, resetRotation);
        }

        public void SetRootPosAndRot(Vector3 position = default, Quaternion rotation = default)
        {
            RootPosition = position;
            RootRotation = rotation;
        }

        /// <summary>
        /// Meshes of this object will be cached based on this name.
        /// </summary>
        public void SetMeshName(string meshName)
        {
            MeshName = meshName;
        }

        public void SetMdl(IModel mdl)
        {
            SetMdh(mdl.Hierarchy);
            SetMdm(mdl.Mesh);
        }

        public void SetMdh(string mdhName)
        {
            Mdh = _resourceCacheService.TryGetModelHierarchy(mdhName);

            if (Mdh == null)
            {
                Logger.LogError($"MDH from name >{mdhName}< for object >{RootGo.name}< not found.", LogCat.Mesh);
            }
        }

        public void SetMdh(IModelHierarchy mdh)
        {
            Mdh = mdh;
        }

        public void SetMdm(string mdmName)
        {
            Mdm = _resourceCacheService.TryGetModelMesh(mdmName);

            if (Mdm == null)
            {
                Logger.LogError($"MDH from name >{mdmName}< for object >{RootGo.name}< not found.", LogCat.Mesh);
            }
        }

        public void SetMdm(IModelMesh mdm)
        {
            Mdm = mdm;
        }

        public void SetMrm(IMultiResolutionMesh mrm)
        {
            Mrm = mrm;
        }

        public void SetMmb(IMorphMesh mmb)
        {
            Mmb = mmb;
        }

        public void SetMrm(string mrmName)
        {
            Mrm = _resourceCacheService.TryGetMultiResolutionMesh(mrmName);

            if (Mrm == null)
            {
                Logger.LogError($"MDH from name >{mrmName}< for object >{RootGo.name}< not found.", LogCat.Mesh);
            }
        }

        /// <summary>
        /// MorphMesh
        /// </summary>
        public void SetMmb(string mmbName)
        {
            // Fix for G1: Damlurker
            if (mmbName == string.Empty)
            {
                return;
            }

            Mmb = _resourceCacheService.TryGetMorphMesh(mmbName);

            if (Mmb == null)
            {
                Logger.LogError($"MMB from name >{mmbName}< for object >{RootGo.name}< not found.", LogCat.Mesh);
            }
        }

        /// <summary>
        /// Only a few objects (vobObjects) have disabled MeshColliders.
        /// </summary>
        public void DisableMeshCollider()
        {
            HasMeshCollider = false;
        }

        /// <summary>
        /// e.g. Vobs and world will use Texture array, but no NPCs or created Vobs after world loading.
        /// </summary>
        public void SetUseTextureArray(bool use)
        {
            UseTextureArray = use;
        }

        public void SetUseColliderCache(bool useColliderCache)
        {
            UseColliderCache = useColliderCache;
        }

        #endregion


        protected void BuildViaMrm()
        {
            CheckPreconditions();
            
            var meshFilter = RootGo.TryAddComponent<MeshFilter>();
            var meshRenderer = RootGo.TryAddComponent<MeshRenderer>();
            meshRenderer.material = Constants.LoadingMaterial;
            PrepareMeshFilter(meshFilter, Mrm, meshRenderer, 0);

            PrepareMeshRenderer(meshRenderer, Mrm);

            if (HasMeshCollider)
            {
                if (UseColliderCache)
                {
                    PrepareCachedCollider(RootGo, meshFilter.sharedMesh);
                }
                else
                {
                    PrepareMeshCollider(RootGo, meshFilter.sharedMesh, Mrm.Materials);
                }
            }

            SetPosAndRot(RootGo, RootPosition, RootRotation);
        }

        protected virtual GameObject[] BuildViaMdmAndMdh()
        {
            CheckPreconditions();
            
            var nodeObjects = new GameObject[Mdh.Nodes.Count];

            // Create empty GameObjects from hierarchy
            {
                for (var i = 0; i < Mdh.Nodes.Count; i++)
                {
                    var node = Mdh.Nodes[i];
                    var nodeName = node.Name;

                    if (TryGetExistingGoInsideNodeHierarchy(Mdh.Nodes, node, out var foundGo))
                    {
                        nodeObjects[i] = foundGo;
                        // Whenever we found a GameObject inside a prefab (pre-existing GameObject),
                        // we rename it as it could potentially contain a regex based name.
                        foundGo!.name = nodeName;
                    }
                    else
                    {
                        nodeObjects[i] = new GameObject(node.Name);
                    }
                }

                // Now set parents
                // HINT: If we found pre-existing nodes, it might be, that the merge between new nodes and existing GOs will reorder them. No issue found so far.
                for (var i = 0; i < Mdh.Nodes.Count; i++)
                {
                    var node = Mdh.Nodes[i];
                    var nodeObj = nodeObjects[i];

                    SetPosAndRot(nodeObj, node.Transform);

                    if (node.ParentIndex == -1)
                    {
                        nodeObj.SetParent(RootGo);
                    }
                    else
                    {
                        nodeObj.SetParent(nodeObjects[node.ParentIndex]);
                    }
                }

                for (var i = 0; i < nodeObjects.Length; i++)
                {
                    if (Mdh.Nodes[i].ParentIndex == -1)
                    {
                        nodeObjects[i].transform.localPosition = Mdh.RootTranslation.ToUnityVector();
                    }
                    else
                    {
                        SetPosAndRot(nodeObjects[i], Mdh.Nodes[i].Transform);
                    }
                }
            }

            //// Fill GameObjects with Meshes from "original" Mesh
            var meshCounter = 0;
            foreach (var softSkinMesh in Mdm.Meshes)
            {
                var mesh = softSkinMesh.Mesh;

                var meshObj = new GameObject($"ZM_{meshCounter}");
                meshObj.SetParent(RootGo);

                var meshFilter = meshObj.AddComponent<MeshFilter>();
                var meshRenderer = meshObj.AddComponent<SkinnedMeshRenderer>();

                meshRenderer.material = Constants.LoadingMaterial;

                // Recalculate bbox based on current bones pose+rot when playing animations.
                // This can become a performance issue which we need to monitor carefully.
                // On top, the name is misleading: updateWhenOffscreen calculates the bbox at all.
                // If not set, then no recalculation is done.
                // @see https://docs.unity3d.com/2022.2/Documentation/Manual/class-SkinnedMeshRenderer.html
                meshRenderer.updateWhenOffscreen = true;

                // HINT: rootBone setting removed. If used, with updateWhenOffscreen e.g. a sitting animation is adding
                //       too much bound size during animation.
                //       Custom AnimationsSystem also played nicely when removing the rootBone.
                // meshRenderer.rootBone = nodeObjects[0].transform;

                PrepareMeshFilter(meshFilter, softSkinMesh, meshRenderer, meshCounter);
                PrepareMeshRenderer(meshRenderer, mesh);

                meshRenderer.sharedMesh = meshFilter.sharedMesh;

                CreateBonesData(RootGo, nodeObjects, meshRenderer, softSkinMesh);

                meshCounter++;
            }

            var attachments = GetFilteredAttachments(Mdm.Attachments);

            // Fill GameObjects with Meshes from attachments
            foreach (var subMesh in attachments)
            {
                var meshObj = nodeObjects.First(bone => bone.name == subMesh.Key);
                var meshFilter = meshObj.TryAddComponent<MeshFilter>();
                var meshRenderer = meshObj.TryAddComponent<MeshRenderer>();
                meshRenderer.material = Constants.LoadingMaterial;

                PrepareMeshFilter(meshFilter, subMesh.Value, meshRenderer, meshCounter);
                PrepareMeshRenderer(meshRenderer, subMesh.Value);
                PrepareMeshCollider(meshObj, meshFilter.sharedMesh, subMesh.Value.Materials);

                // As Attachments are also just meshes, we need to increase the mesh counter for Filter's meshCache index.
                meshCounter++;
            }

            SetPosAndRot(RootGo, RootPosition, RootRotation);

            // We need to set the root translation after we add children above. Otherwise the "additive" position/rotation will be broken.
            // We need to reset the rootBones position to zero. Otherwise Vobs won't be placed right.
            // Due to Unity's parent-child transformation magic, we need to do it at the end. ╰(*°▽°*)╯
            nodeObjects[0].transform.localPosition = Vector3.zero;

            return nodeObjects;
        }

        protected GameObject BuildViaMmb()
        {
            CheckPreconditions();

            var meshFilter = RootGo.TryAddComponent<MeshFilter>();
            var meshRenderer = RootGo.TryAddComponent<MeshRenderer>();

            PrepareMeshFilter(meshFilter, Mmb.Mesh, meshRenderer, 0);
            PrepareMeshRenderer(meshRenderer, Mmb.Mesh);

            SetPosAndRot(RootGo, RootPosition, RootRotation);

            return RootGo;
        }

        protected void CheckPreconditions()
        {
            if (RootGo == null)
            {
                throw new ArgumentNullException("Main GameObject is null. Please provide one or force creation " +
                                                "of a new one via SetGameObject().");
            }
            
            if (MeshName.IsNullOrEmpty())
            {
                throw new ArgumentNullException($"No MeshName for >{RootGo.name} provided. " +
                                            "Please provide a mesh name via SetMeshName() which is used for caching of mesh data at runtime.");
            }
        }

        protected void PrepareMeshRenderer(Renderer rend, IMultiResolutionMesh mrmData)
        {
            if (null == mrmData)
            {
                Logger.LogError($"No mesh data could be added to renderer: {rend.transform.parent.name}", LogCat.Mesh);
                return;
            }

            if (rend is MeshRenderer && !rend.GetComponent<MeshFilter>().sharedMesh)
            {
                Logger.LogError($"Null mesh on {rend.gameObject.name}", LogCat.Mesh);
                return;
            }

            var finalMaterials = new List<Material>(mrmData.SubMeshes.Count);
            var submeshCount = rend is MeshRenderer
                ? rend.GetComponent<MeshFilter>().sharedMesh.subMeshCount
                : mrmData.SubMeshCount;

            for (var i = 0; i < submeshCount; i++)
            {
                var materialData = mrmData.SubMeshes[i].Material;
                if (materialData.Texture.IsEmpty()) // No texture to add.
                {
                    Logger.LogWarning("No texture was set for: " + materialData.Name, LogCat.Mesh);
                    return;
                }

                Texture texture;
                TextureCacheService.TextureArrayTypes textureType;

                if (UseTextureArray)
                {
                    TextureCacheService.GetTextureArrayEntry(materialData.Texture, out texture, out textureType);
                }
                else
                {
                    texture = GetTexture(materialData.Texture);
                    textureType = TextureCacheService.TextureArrayTypes.Unknown;
                }

                // TODO - G1: Skeleton warrior's second texture doesn't exist. No alternatives needed/given.
                // TODO - Therefore consider removing this warning in the future.
                if (!texture)
                {
                    Logger.LogWarning($"Couldn't get texture from name: {materialData.Texture}", LogCat.Mesh);
                    continue;
                }

                var material = GetDefaultMaterial(textureType);

                material.mainTexture = texture;
                rend.material = material;
                finalMaterials.Add(material);
            }

            rend.SetMaterials(finalMaterials);
        }

        /// <summary>
        /// Ok, brace yourself:
        /// There are three parameters of interest when it comes to creating meshes for items (etc.).
        /// 1. positions - Unity: vertices (=Vector3)
        /// 2. triangles - contains 3 indices to wedges.
        /// 3. wedges - contains indices (Unity: triangles) to the positions (Unity: vertices) and textures (Unity: uvs (=Vector2)).
        ///
        /// Data example:
        ///  positions: 0=>[x1,x2,x3], 0=>[x2,y2,z2], 0=>[x3,y3,z3]
        ///  subMesh:
        ///    triangles: [0, 2, 1], [1, 2, 3]
        ///    wedges: 0=>[index=0, texture=...], 1=>[index=2, texture=...], 2=>[index=2, texture=...]
        ///
        ///  If we now take first triangle and prepare it for Unity, we would get the following:
        ///  vertices = 0[x0,...], 2[x2,...], 1[x1,...] --> as triangles point to a wedge and wedge index points to the position-index itself.
        ///  triangles = 0, 2, 3 --> (indices for position items); ATTENTION: index 3 would normally be index 2, but! we can never reuse positions. We always need to create new ones. (Reason: uvs demand the same size as vertices.)
        ///  uvs = [wedge[0].texture], [wedge[2].texture], [wedge[1].texture]
        /// </summary>
        protected void PrepareMeshFilter(MeshFilter meshFilter, IMultiResolutionMesh mrmData, Renderer meshRenderer, int meshIndex, List<System.Numerics.Vector3> calculatedVertices = null)
        {
            // ISoftSkinMeshes will be prepared before reaching this method. This is due to NPC armors having dedicated offsets per item.
            calculatedVertices ??= mrmData.Positions;

            var subMeshPerTextureFormat = new Dictionary<TextureCacheService.TextureArrayTypes, int>();

            // Elements like NPC armors might have multiple meshes. We therefore need to store each mesh with it's associated index.
            if (_multiTypeCacheService.Meshes.TryGetValue($"{MeshName}_{meshIndex}", out Mesh mesh))
            {
                meshFilter.sharedMesh = mesh;
                return;
            }

            mesh = new Mesh { name = MeshName };
            meshFilter.sharedMesh = mesh;

            if (null == mrmData)
            {
                Logger.LogError($"No mesh data could be added to filter: {meshFilter.transform.parent.name}", LogCat.Mesh);
                return;
            }

            CreateMorphMeshBegin(mrmData, mesh);

            int triangleCount = mrmData.SubMeshes.Sum(i => i.Triangles.Count);
            int vertexCount = triangleCount * 3;
            int index = 0;
            var preparedVertices = new List<Vector3>(vertexCount);
            var preparedUVs = new List<Vector4>(vertexCount);
            var normals = new List<Vector3>(vertexCount);
            var preparedTriangles = new List<List<int>>();

            foreach (var subMesh in mrmData.SubMeshes)
            {
                // When using the texture array, get the index of the array of the matching texture format. Build sub meshes for each texture format, i.e. separating opaque and alpha cutout textures.
                var textureArrayIndex = 0;
                var maxMipLevel = 0;
                var textureScale = Vector2.one;
                var textureArrayType = TextureCacheService.TextureArrayTypes.Opaque;
                if (UseTextureArray)
                {
                    TextureCacheService.GetTextureArrayIndex(subMesh.Material, out textureArrayType, out textureArrayIndex, out textureScale, out maxMipLevel, out _);
                    if (!subMeshPerTextureFormat.ContainsKey(textureArrayType))
                    {
                        subMeshPerTextureFormat.Add(textureArrayType, preparedTriangles.Count);
                        preparedTriangles.Add(new List<int>());
                    }
                }
                else
                {
                    preparedTriangles.Add(new List<int>());
                }

                for (var i = 0; i < subMesh.Triangles.Count; i++)
                {
                    // One triangle is made of 3 elements for Unity. We therefore need to prepare 3 elements within one loop.
                    MeshWedge[] wedges =
                    {
                        subMesh.Wedges[subMesh.Triangles[i].Wedge2], subMesh.Wedges[subMesh.Triangles[i].Wedge1],
                        subMesh.Wedges[subMesh.Triangles[i].Wedge0]
                    };

                    for (var w = 0; w < wedges.Length; w++)
                    {
                        preparedVertices.Add(calculatedVertices[wedges[w].Index].ToUnityVector());
                        if (UseTextureArray)
                        {
                            preparedTriangles[subMeshPerTextureFormat[textureArrayType]].Add(index++);
                        }
                        else
                        {
                            preparedTriangles[preparedTriangles.Count - 1].Add(index++);
                        }

                        normals.Add(wedges[w].Normal.ToUnityVector());
                        var uv = Vector2.Scale(textureScale, wedges[w].Texture.ToUnityVector());
                        preparedUVs.Add(new Vector4(uv.x, uv.y, textureArrayIndex, maxMipLevel));

                        CreateMorphMeshEntry(wedges[w].Index, preparedVertices.Count);
                    }
                }
            }

            // Unity 1/ handles vertices on mesh level, but triangles (aka vertex-indices) on submesh level.
            // and 2/ demands vertices to be stored before triangles/uvs.
            // Therefore, we prepare the full data once and assign it afterward.
            // @see: https://answers.unity.com/questions/531968/submesh-vertices.html
            mesh.subMeshCount = preparedTriangles.Count;
            mesh.SetVertices(preparedVertices);
            mesh.SetUVs(0, preparedUVs);
            mesh.SetNormals(normals);
            for (var i = 0; i < preparedTriangles.Count; i++)
            {
                mesh.SetTriangles(preparedTriangles[i], i);
            }

            CreateMorphMeshEnd(preparedVertices);

            _multiTypeCacheService.Meshes.Add($"{MeshName}_{meshIndex}", mesh);
        }

        protected void PrepareMeshFilter(MeshFilter meshFilter, ISoftSkinMesh soft, Renderer renderer, int meshIndex)
        {
            var vertices = GetSoftSkinMeshPositions(soft);

            // Delegate actual mesh filter creation to function handling the MRM itself.
            PrepareMeshFilter(meshFilter, soft.Mesh, renderer, meshIndex, vertices);


            // Now let's add bone data.
            var zkMesh = soft.Mesh;
            var weights = soft.Weights;

            var verticesAndUvSize = zkMesh.SubMeshes.Sum(i => i.Triangles!.Count) * 3;
            var preparedBoneWeights = new List<BoneWeight>(verticesAndUvSize);

            foreach (var subMesh in zkMesh.SubMeshes)
            {
                var triangles = subMesh.Triangles;
                var wedges = subMesh.Wedges;

                for (var i = 0; i < triangles.Count; i++)
                {
                    var index1 = wedges![triangles[i].Wedge2];
                    var index2 = wedges[triangles[i].Wedge1];
                    var index3 = wedges[triangles[i].Wedge0];

                    preparedBoneWeights.Add(weights[index1.Index].ToBoneWeight(soft.Nodes));
                    preparedBoneWeights.Add(weights[index2.Index].ToBoneWeight(soft.Nodes));
                    preparedBoneWeights.Add(weights[index3.Index].ToBoneWeight(soft.Nodes));
                }
            }

            // PrepareMeshFilter() on top filled the mesh itself. We now need to set bone data only!
            meshFilter.sharedMesh.boneWeights = preparedBoneWeights.ToArray();
        }

        protected virtual List<System.Numerics.Vector3> GetSoftSkinMeshPositions(ISoftSkinMesh softSkinMesh)
        {
            return softSkinMesh.Mesh.Positions;
        }

        /// <summary>
        /// We basically only set the values from official Unity documentation. No added sugar for the bingPoses.
        /// @see https://docs.unity3d.com/ScriptReference/Mesh-bindposes.html
        /// @see https://forum.unity.com/threads/some-explanations-on-bindposes.86185/
        /// </summary>
        private void CreateBonesData(GameObject rootObj, GameObject[] nodeObjects, SkinnedMeshRenderer renderer,
            ISoftSkinMesh mesh)
        {
            var meshBones = new Transform[mesh.Nodes.Count];
            var bindPoses = new UnityEngine.Matrix4x4[mesh.Nodes.Count];

            for (var i = 0; i < mesh.Nodes.Count; i++)
            {
                var nodeIndex = mesh.Nodes[i];

                meshBones[i] = nodeObjects[nodeIndex].transform;
                bindPoses[i] = meshBones[i].worldToLocalMatrix * rootObj.transform.localToWorldMatrix;
            }

            renderer.sharedMesh.bindposes = bindPoses;
            renderer.bones = meshBones;
        }

        protected Collider PrepareMeshCollider(GameObject obj, Mesh mesh)
        {
            var meshCollider = obj.TryAddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            return meshCollider;
        }

        /// <summary>
        /// Check if Collider needs to be added.
        /// </summary>
        [Obsolete("Used for previous WorldMeshBuilder from 2024. Can be removed.")]
        protected Collider PrepareMeshCollider(GameObject obj, Mesh mesh, IMaterial materialData)
        {
            if (materialData.DisableCollision ||
                materialData.Group == MaterialGroup.Water)
            {
                // Do not add colliders
                return null;
            }

            return PrepareMeshCollider(obj, mesh);
        }

        /// <summary>
        /// Check if Collider needs to be added.
        /// </summary>
        protected void PrepareMeshCollider(GameObject obj, Mesh mesh, List<IMaterial> materialDatas)
        {
            var anythingDisableCollission = materialDatas.Any(i => i.DisableCollision);
            var anythingWater = materialDatas.Any(i => i.Group == MaterialGroup.Water);

            if (!anythingDisableCollission && !anythingWater)
            {
                PrepareMeshCollider(obj, mesh);
            }
        }
        
        protected void PrepareCachedCollider(GameObject rootGo, Mesh mesh)
        {
            if (!_staticCacheService.LoadedVobItemColliders.TryGetValue(MeshName, out var colliders))
            {
                Logger.LogError($"Can't find Collider data for {MeshName}. Skipping...", LogCat.Mesh);
                return;
            }

            foreach (var coll in colliders)
            {
                if (coll.T == VobItemColliderCacheCreatorDomain.T.B)
                {
                    var boxCollider = rootGo.AddComponent<BoxCollider>();
                    boxCollider.center = coll.C;
                    boxCollider.size = coll.S;
                }
                else if (coll.T == VobItemColliderCacheCreatorDomain.T.C)
                {
                    var capsuleCollider = rootGo.AddComponent<CapsuleCollider>();
                    capsuleCollider.center = coll.C;
                    capsuleCollider.direction = coll.D;
                    capsuleCollider.height = coll.H;
                    capsuleCollider.radius = coll.R;
                }
                else
                {
                    Logger.LogError($"Capsule cache type {coll.T} not yet handled", LogCat.Mesh);
                }
            }
        }

        private void CreateMorphMeshBegin(IMultiResolutionMesh mrm, Mesh mesh)
        {
            if (Mmb == null)
            {
                return;
            }

            // MorphMeshes will change the vertices. This call optimizes performance.
            mesh.MarkDynamic();

            IsMorphMeshMappingAlreadyCached = _morphMeshCacheService.IsMappingAlreadyCached(Mmb.Name);
            if (IsMorphMeshMappingAlreadyCached)
            {
                return;
            }

            _morphMeshCacheService.AddVertexMapping(Mmb.Name, mrm.PositionCount);
        }

        private void CreateMorphMeshEntry(int index1, int preparedVerticesCount)
        {
            // We add mapping data to later reuse for IMorphAnimation samples
            if (Mmb == null || IsMorphMeshMappingAlreadyCached)
            {
                return;
            }

            _morphMeshCacheService.AddVertexMappingEntry(Mmb.Name, index1, preparedVerticesCount - 1);
        }

        private void CreateMorphMeshEnd(List<Vector3> preparedVertices)
        {
            if (Mmb == null || IsMorphMeshMappingAlreadyCached)
            {
                return;
            }

            _morphMeshCacheService.SetUnityVerticesForVertexMapping(Mmb.Name, preparedVertices.ToArray());
        }

        /// <summary>
        /// There are some objects (e.g. NPCs) where we want to skip specific attachments. This method can be overridden for this feature.
        /// </summary>
        protected virtual Dictionary<string, IMultiResolutionMesh> GetFilteredAttachments(
            Dictionary<string, IMultiResolutionMesh> attachments)
        {
            return attachments;
        }

        protected virtual Texture2D GetTexture(string name)
        {
            return TextureCacheService.TryGetTexture(name);
        }

        private Material GetDefaultMaterial(TextureCacheService.TextureArrayTypes textureType)
        {
            if (UseTextureArray)
            {
                Shader shader;
                switch (textureType)
                {
                    case TextureCacheService.TextureArrayTypes.Opaque:
                        shader = Constants.ShaderWorldLit;
                        break;
                    case TextureCacheService.TextureArrayTypes.Transparent:
                        // Cutout for e.g. bushes.
                        shader = Constants.ShaderLitAlphaToCoverage;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(textureType), textureType, null);
                }

                var material = new Material(shader);
                return material;
            }
            else
            {
                return new Material(Constants.ShaderSingleMeshLit);
            }
        }

        protected Material GetWaterMaterial()
        {
            var material = new Material(Constants.ShaderWater);
            // Manually correct the render queue for alpha test, as Unity doesn't want to do it from the shader's render queue tag.
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        protected void SetPosAndRot(GameObject obj, Matrix4x4 matrix)
        {
            SetPosAndRot(obj, matrix.ToUnityMatrix());
        }

        protected void SetPosAndRot(GameObject obj, UnityEngine.Matrix4x4 matrix)
        {
            SetPosAndRot(obj, matrix.GetPosition() / 100, matrix.rotation);
        }

        protected void SetPosAndRot(GameObject obj, Vector3 position, Quaternion rotation)
        {
            obj.transform.SetLocalPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// Check if the current node already exists inside the existing GameObject starting with RootGO.
        ///
        /// Lookup order:
        /// Nodes: from bottom to top, then compared with GOs in reverse order (top to bottom)
        /// GameObjects: From top to bottom
        ///
        /// Example GOs (from a Prefab):
        /// BIP01
        ///   |- BIP01 CHEST_.*_1
        ///
        /// Example node hierarchy:
        /// BIP01
        ///   |- BIP01 CHESTLOCK
        ///   |- BIP01 CHEST_SMALL_1
        ///
        /// Merged GOs:
        /// BIP01
        ///   |- BIP01 CHEST_SMALL_1 - from prefab; but the order changed later, when glued together with new GOs. No issue on that so far.
        ///   |- BIP01 CHESTLOCK     - from nodes; new
        /// </summary>
        private bool TryGetExistingGoInsideNodeHierarchy(List<IModelHierarchyNode> nodes,
            IModelHierarchyNode currentNode, [CanBeNull] out GameObject foundGo)
        {
            // We use a stack to have LI-FO approach. When we loop, we need to start from root (last entry), not bottom (first entry).
            var loopHierarchy = new Stack<IModelHierarchyNode>();
            loopHierarchy.Push(currentNode);

            // Fetch nodes all the way up.
            {
                var walkedNode = currentNode;
                while (walkedNode.ParentIndex != -1)
                {
                    walkedNode = nodes[walkedNode.ParentIndex];
                    loopHierarchy.Push(walkedNode);
                }
            }

            var currentGo = RootGo; // Start from top to bottom
            var childFound = false;

            // Loop through the nodes from top to bottom and check if there's always a matching GO.
            while (!loopHierarchy.IsEmpty())
            {
                foreach (var childGo in currentGo.GetAllDirectChildren())
                {
                    // Fast comparison - If name is already matching NodeName from Gothic data, we are fine.
                    if (childGo.name.Equals(loopHierarchy.Peek().Name))
                    {
                        currentGo = childGo;
                        childFound = true;
                        break;
                    }

                    // Second comparison - We need to check if the name of this GO is matching a regex pattern for NodeName
                    // e.g. BIP01 CHEST_.*_1 --> matches --> BIP01 CHEST_SMALL_1
                    var regex = new Regex(childGo.name, RegexOptions.IgnoreCase);
                    if (regex.IsMatch(loopHierarchy.Peek().Name))
                    {
                        currentGo = childGo;
                        childFound = true;
                        break;
                    }
                }

                // If - at some point - the loop ended
                if (!childFound)
                {
                    foundGo = null;
                    return false;
                }

                childFound = false; // We are not done yet and need to check next iteration again.
                loopHierarchy.Pop();
            }

            // We looped all the way from root to leaf and every time, we got a matching element from existing GameObjects.
            foundGo = currentGo;
            return true;
        }
    }
}
