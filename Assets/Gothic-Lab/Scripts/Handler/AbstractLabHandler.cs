using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Lab.Handler
{
    public abstract class AbstractLabHandler : MonoBehaviour
    {
        public abstract void Bootstrap();


        [Inject] protected readonly VmCacheService VmCacheService;
        [Inject] protected readonly MeshService MeshService;
        [Inject] protected readonly VobService VobService;
        [Inject] protected readonly GameStateService GameStateService;
        [Inject] protected readonly ResourceCacheService ResourceCacheService;


        protected GameObject SpawnInteractable(string mdlName, PrefabType type, GameObject parentGo, Vector3 position = default, Quaternion rotation = default)
        {
            var prefab = ResourceCacheService.TryGetPrefabObject(type);
            var mdl = ResourceCacheService.TryGetModel(mdlName);

            if (mdl == null)
            {
                Logger.LogError("LabInteractableHandler: Element has no .mdl file: " + mdlName, LogCat.Debug);
                return null;
            }

            return MeshService.CreateVob(mdlName, mdl, position, rotation,
                rootGo: prefab, parent: parentGo, useTextureArray: false);
        }
        
        protected GameObject SpawnItem(string itemName, GameObject parentGo, Vector3 position = default, PrefabType type = PrefabType.VobItem)
        {
            var itemPrefab = ResourceCacheService.TryGetPrefabObject(type);
            var item = VmCacheService.TryGetItemData(itemName);
            var mrm = ResourceCacheService.TryGetMultiResolutionMesh(item.Visual);
            var itemGo = MeshService.CreateVob(item.Visual, mrm, position, default, true,
                rootGo: itemPrefab, parent: parentGo, useTextureArray: false);

            return itemGo;
        }
    }
}
