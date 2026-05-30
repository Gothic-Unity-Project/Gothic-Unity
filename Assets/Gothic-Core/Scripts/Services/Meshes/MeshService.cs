using System.Linq;
using System.Threading.Tasks;
using GUZ.Core.Domain.Meshes.Builder;
using GUZ.Core.Extensions;
using GUZ.Core.Manager;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services.StaticCache;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;

namespace GUZ.Core.Services.Meshes
{
    public class MeshService
    {
        public async Task CreateWorld(StaticCacheService.WorldChunkContainer worldChunks, IMesh mesh,
            LoadingService loading, GameObject rootGo, bool useTextureArray = true)
        {
            var worldBuilder = new WorldMeshBuilder().Inject();
            worldBuilder.SetGameObject(rootGo);
            worldBuilder.SetWorldData(worldChunks, mesh);
            worldBuilder.SetUseTextureArray(useTextureArray);

            await worldBuilder.BuildAsync(loading);
        }

        public GameObject CreateNpc(string npcName, string mdmName, string mdhName, ExtSetVisualBodyData bodyData,
            Vector3 position = default, Quaternion rotation = default,
            GameObject parent = null, GameObject root = null)
        {
            var npcBuilder = new NpcMeshBuilder().Inject();
            npcBuilder.SetRootPosAndRot(position, rotation);
            npcBuilder.SetGameObject(root, npcName);
            npcBuilder.SetParent(parent);
            npcBuilder.SetMeshName(mdmName);
            npcBuilder.SetMdh(mdhName);
            npcBuilder.SetMdm(mdmName);
            npcBuilder.SetBodyData(bodyData);

            var npcGo = npcBuilder.Build();

            var npcHeadBuilder = new NpcHeadMeshBuilder().Inject();
            npcHeadBuilder.SetGameObject(npcGo);
            npcHeadBuilder.SetBodyData(bodyData);
            npcHeadBuilder.SetMeshName(bodyData.Head);
            npcHeadBuilder.SetMmb(bodyData.Head);

            npcHeadBuilder.Build();

            return npcGo;
        }

         public GameObject CreateNpcWeapon(GameObject npcGo, ItemInstance itemData,
            VmGothicEnums.ItemFlags mainFlag, VmGothicEnums.ItemFlags flags, bool isEquipped = false)
        {
            var npcWeaponBuilder = new NpcWeaponMeshBuilder().Inject();
            npcWeaponBuilder.SetWeaponData(npcGo, itemData, mainFlag, flags, isEquipped);
            npcWeaponBuilder.SetMeshName(itemData.Visual);

            switch (mainFlag)
            {
                case VmGothicEnums.ItemFlags.ItemKatNf:
                    npcWeaponBuilder.SetMrm(itemData.Visual);
                    break;
                case VmGothicEnums.ItemFlags.ItemKatFf:
                    npcWeaponBuilder.SetMmb(itemData.Visual);
                    break;
                default:
                    // NOP - e.g. for armor.
                    return null;
            }

            return npcWeaponBuilder.Build();
        }

         public GameObject CreateVob(string objectName, IMultiResolutionMesh mrm,
            Vector3 position = default, Quaternion rotation = default, bool withCollider = true,
            GameObject parent = null, GameObject rootGo = null, bool useTextureArray = true, bool useColliderCache = false)
        {
            if (!HasTextures(mrm))
            {
                return null;
            }

            var vobBuilder = new VobMeshBuilder().Inject();
            vobBuilder.SetRootPosAndRot(position, rotation);
            vobBuilder.SetGameObject(rootGo, objectName);
            vobBuilder.SetParent(parent, resetRotation: true);
            vobBuilder.SetMeshName(objectName);
            vobBuilder.SetMrm(mrm);
            vobBuilder.SetUseTextureArray(useTextureArray);
            vobBuilder.SetUseColliderCache(useColliderCache);

            if (!withCollider)
            {
                vobBuilder.DisableMeshCollider();
            }

            return vobBuilder.Build();
        }

         public GameObject CreateVob(string objectName, IModel mdl,
            Vector3 position = default, Quaternion rotation = default,
            GameObject parent = null, GameObject rootGo = null, bool useTextureArray = true)
        {
            if (!HasMeshes(mdl.Mesh))
            {
                return null;
            }

            var vobBuilder = new VobMeshBuilder().Inject();
            vobBuilder.SetRootPosAndRot(position, rotation);
            vobBuilder.SetGameObject(rootGo, objectName);
            vobBuilder.SetParent(parent, resetRotation: true); // If we don't reset these, all objects will be rotated wrong!
            vobBuilder.SetMeshName(objectName);
            vobBuilder.SetMdl(mdl);
            vobBuilder.SetUseTextureArray(useTextureArray);

            return vobBuilder.Build();
        }

         public GameObject CreateVob(string objectName, IMorphMesh mmb,
            Vector3 position = default, Quaternion rotation = default,
            GameObject parent = null, GameObject rootGo = null, bool useTextureArray = false,
            bool useColliderCache = false)
        {
            var vobBuilder = new VobMeshBuilder().Inject();
            vobBuilder.SetRootPosAndRot(position, rotation);
            vobBuilder.SetGameObject(rootGo, objectName);
            vobBuilder.SetParent(parent);
            vobBuilder.SetMeshName(objectName);
            vobBuilder.SetMmb(mmb);
            vobBuilder.SetUseTextureArray(useTextureArray);
            vobBuilder.SetUseColliderCache(useColliderCache);

            return vobBuilder.Build();
        }

         public GameObject CreateVob(string objectName, IModelMesh mdm, IModelHierarchy mdh,
            Vector3 position = default, Quaternion rotation = default,
            GameObject parent = null, GameObject rootGo = null, bool useTextureArray = true)
        {
            if (!HasMeshes(mdm))
            {
                return null;
            }

            var vobBuilder = new VobMeshBuilder().Inject();
            vobBuilder.SetRootPosAndRot(position, rotation);
            vobBuilder.SetGameObject(rootGo, objectName);
            vobBuilder.SetParent(parent, resetRotation: true);
            vobBuilder.SetMeshName(objectName);
            vobBuilder.SetMdh(mdh);
            vobBuilder.SetMdm(mdm);
            vobBuilder.SetUseTextureArray(useTextureArray);

            return vobBuilder.Build();
        }

        private static bool HasTextures(IMultiResolutionMesh mrm)
        {
            // If there is no texture for any of the meshes, just skip this item.
            // G1: Some skull decorations (OC_DECORATE_V4.3DS) are without texture.
            return !mrm.Materials.All(m => m.Texture.IsEmpty());
        }

        private static bool HasMeshes(IModelMesh mdm)
        {
            // Check if there are completely empty elements without any texture.
            // G1: e.g. Harp, Flute, and WASH_SLOT (usage moved to a FreePoint within daedalus functions)
            var noMeshTextures =
                mdm.Meshes.All(mesh => mesh.Mesh.SubMeshes.All(subMesh => subMesh.Material.Texture.IsEmpty()));
            var noAttachmentTextures =
                mdm.Attachments.All(att => att.Value.Materials.All(mat => mat.Texture.IsEmpty()));

            return !(noMeshTextures && noAttachmentTextures);
        }

        public GameObject CreateVobPfx(IVirtualObject vob, Vector3 position = default, Quaternion rotation = default, GameObject parent = null)
        {
            return CreateVobPfx(vob.Visual!.Name, position, rotation, parent);
        }

        public GameObject CreateVobPfx(string pfxName, Vector3 position = default, Quaternion rotation = default, GameObject parent = null, bool destroyAfterPlay = false)
        {
            var vobPfxBuilder = new VobPfxMeshBuilder().Inject();
            vobPfxBuilder.SetGameObject(null, pfxName);
            vobPfxBuilder.SetParent(parent);
            vobPfxBuilder.SetRootPosAndRot(position, rotation);
            vobPfxBuilder.SetPfxData(pfxName);
            vobPfxBuilder.SetDestroyAfterPlay(destroyAfterPlay);

            return vobPfxBuilder.Build();
        }

        public GameObject CreateVobDecal(IVirtualObject vob, VisualDecal decal, Vector3 position = default, Quaternion rotation = default, GameObject parent = null)
        {
            var vobDecalBuilder = new VobDecalMeshBuilder().Inject();
            vobDecalBuilder.SetGameObject(null, vob.Name);
            vobDecalBuilder.SetParent(parent);
            vobDecalBuilder.SetRootPosAndRot(position, rotation);
            vobDecalBuilder.SetDecalData(vob, decal);

            return vobDecalBuilder.Build();
        }

         public async Task CreateTextureArray()
        {
            await new TextureArrayBuilder().Inject().BuildAsync();
        }

         public GameObject CreateBarrier(string objectName, IMesh mesh)
        {
            var barrierBuilder = new BarrierMeshBuilder().Inject();
            barrierBuilder.SetGameObject(null, objectName);
            barrierBuilder.SetBarrierMesh(mesh);

            return barrierBuilder.Build();
        }

         public GameObject CreatePolyStrip(GameObject go, int numberOfSegments, Vector3 startPoint,
            Vector3 endPoint)
        {
            var polyStripBuilder = new PolyStripMeshBuilder().Inject();
            polyStripBuilder.SetGameObject(go);
            polyStripBuilder.SetPolyStripData(numberOfSegments, startPoint, endPoint);

            return polyStripBuilder.Build();
        }
    }
}
