using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gothic.Core.Adapters.UI.LoadingBars;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Creator;
using Gothic.Core.Domain.Vobs;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Config;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.World;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Npc;
using JetBrains.Annotations;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Services.Vobs
{
    public class VobService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly FrameSkipperService _frameSkipperService;
        [Inject] private readonly VobSoundCullingService _vobSoundCullingService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        
        // Supporter class where the whole Init() logic is outsourced for better readability.
        private readonly VobInitializerDomain _initializerDomain = new VobInitializerDomain().Inject();


        public Dictionary<string, List<(int hour, int minute, int status)>> ObjectRoutines = new();
        
        private const float _interactableLookupDistance = 10f; // meter
        
        private readonly char[] _itemNameSeparators = { ';', ',' };
        private readonly char[] _itemCountSeparators = { ':', '.' };


        private const int VobInitBatchSize = 10;

        private Dictionary<VirtualObjectType, GameObject> _vobTypeParentGOs = new();
        private Queue<VobLoader> _objectsToInitQueue = new();

        // Important: All of them are not culled!
        private static readonly VirtualObjectType[] _vobTypesNonLazyLoading =
        {
            // FIXME - As we want to cull these two, we can simply lazyLoad them
            // VirtualObjectType.zCVobSound,
            // VirtualObjectType.zCVobSoundDaytime,
            VirtualObjectType.oCZoneMusic,
            VirtualObjectType.oCZoneMusicDefault,
            VirtualObjectType.zCVobSpot,
            VirtualObjectType.zCVobStartpoint,
            VirtualObjectType.oCTriggerChangeLevel,
            VirtualObjectType.zCMover,
            VirtualObjectType.zCPFXController,
            VirtualObjectType.zCTriggerList,
            VirtualObjectType.oCTriggerScript,
            VirtualObjectType.zCVobLevelCompo
        };

        public void Init()
        {
            _unityMonoService.StartCoroutine(InitVobCoroutine());
            GlobalEventDispatcher.LoadGameStart.AddListener(PreWorldCreate);

            // Decoupling Culling logic from actual init logic.
            GlobalEventDispatcher.VobMeshCullingChanged.AddListener(InitVob);
            GlobalEventDispatcher.LockPickComboBroken.AddListener((lockPick, _, _) => lockPick.VobAs<IItem>().Amount--);
            GlobalEventDispatcher.LockPickComboFinished.AddListener((_, containerOrDoor, _) =>
            {
                if (containerOrDoor.Vob is IContainer container)
                    container.IsLocked = false;
                else if (containerOrDoor.Vob is IDoor door)
                    door.IsLocked = false;
            });
        }

        public void PreWorldCreate()
        {
            _gameStateService.VobsInteractable.Clear();
            _gameStateService.VobsMover.Clear();
        }

        /// <summary>
        /// Load VOBs during world creation. The VOBs itself are then lazy loaded (i.e. when Culling kicks in, a Loading component
        /// will take care of initializing later) or some of them are loaded immediately (e.g. Spots).
        /// </summary>
        public async Task CreateWorldVobsAsync(DeveloperConfig config, LoadingService loading, List<IVirtualObject> vobs, GameObject root)
        {
            PreCreateWorldVobs(vobs, root, loading);
            await CreateWorldVobs(config, loading, vobs);

            // Ensure all Vob skeletons are created.
            await Task.Yield();

            PostCreateWorldVobs();
        }

        public GameObject GetRootGameObjectOfType(VirtualObjectType type)
        {
            if (_vobTypeParentGOs.IsEmpty())
                return null; // e.g., within Lab or as fallback on errors.
            
            if (_vobTypeParentGOs.TryGetValue(type, out var parentGo))
            {
                return parentGo;
            }
            else
            {
                Logger.LogError($"No suitable root GO found for type >{type}<", LogCat.Vob);
                return null;
            }
        }

        /// <summary>
        /// Some VOBs are initialized eagerly (e.g. when there is no performance benefit in doing so later or its needed directly).
        /// </summary>
        public void InitVobNow(VobContainer container)
        {
            _initializerDomain.InitVob(container.Vob, container.Go, default, true);
        }
        
        /// <summary>
        /// First time a VOB is made visible: Create it.
        /// </summary>
        public void InitVob(GameObject go)
        {
            go.TryGetComponent(out VobLoader loaderComp);

            // IsQueued ensures we do not add elements to be loaded twice (without an O(n) Queue.Contains() check).
            if (loaderComp == null || loaderComp.IsLoaded || loaderComp.IsQueued)
            {
                return;
            }

            loaderComp.IsQueued = true;
            _objectsToInitQueue.Enqueue(loaderComp);
        }

        // DEBUG - Check how many frames it took to initialize all the objects
        // private int firstFrameQueueFilledUp;

        private IEnumerator InitVobCoroutine()
        {
            while (true)
            {
                if (_objectsToInitQueue.IsEmpty())
                {
                    // DEBUG
                    // if (firstFrameQueueFilledUp != 0)
                    // {
                    //     Logger.LogWarning($"It took {Time.frameCount - firstFrameQueueFilledUp} frames to clear the queue.");
                    //     firstFrameQueueFilledUp = 0;
                    // }
                    yield return null;
                }
                else
                {
                    // DEBUG
                    // if (firstFrameQueueFilledUp == 0)
                    // {
                    //     firstFrameQueueFilledUp = Time.frameCount;
                    // }

                    for (var i = 0; i < VobInitBatchSize && !_objectsToInitQueue.IsEmpty(); i++)
                    {
                        var item = _objectsToInitQueue.Dequeue();

                        // Destroyed in the meantime (e.g. world change).
                        if (item == null)
                            continue;

                        item.IsLoaded = true;

                        // We assume that each loaded VOB is centered at parent=0,0,0.
                        // Should work smoothly until we start lazy loading sub-vobs ;-)
                        try
                        {
                            _initializerDomain.InitVob(item.Container.Vob, item.gameObject, default, true);
                        }
                        catch (Exception e)
                        {
                            Logger.LogError($"Failed to init VOB {item.name}: {e}", LogCat.Vob);
                        }

                    }

                    yield return _frameSkipperService.TrySkipToNextFrameCoroutine();
                }
            }
        }

        /// <summary>
        /// Create item with mesh only. No special handling like grabbing etc.
        /// e.g. used for NPCs drinking beer mesh in their hand.
        /// </summary>
        public void CreateItemMesh(int itemId, GameObject parentGo)
        {
            if (itemId == -1)
            {
                Logger.LogError("No ItemId found. Is this a bug on daedalus or our side?", LogCat.Vob);
                return; // no item
            }
            var item = _vmCacheService.TryGetItemData(itemId);

            _initializerDomain.CreateItemMesh(item, parentGo, default);
        }

        /// <summary>
        /// Create item with mesh only. No special handling like grabbing etc.
        /// e.g. used for NPCs drinking beer mesh in their hand.
        /// </summary>
        public void CreateItemMesh(string itemName, GameObject parentGo)
        {
            if (itemName == "")
            {
                return;
            }

            var item = _vmCacheService.TryGetItemData(itemName);

            _initializerDomain.CreateItemMesh(item, parentGo, default);
        }

        /// <summary>
        /// To save memory, we can also Destroy Vobs and their Mesh+GO structure.
        /// </summary>
        public void DestroyVob(GameObject go)
        {
            throw new NotImplementedException();
        }
        
        private void PreCreateWorldVobs(List<IVirtualObject> vobs, GameObject rootGo, LoadingService loading)
        {
            loading.SetPhase(nameof(WorldLoadingBarHandler.ProgressType.VOB), GetTotalVobCount(vobs));

            // We reset the GO dictionary.
            _vobTypeParentGOs = new();

            // Drop pending lazy-init entries from a previous world. Their VobLoader GOs are destroyed by now.
            _objectsToInitQueue.Clear();

            ObjectRoutines.ClearAndReleaseMemory();
            ObjectRoutines = new();

            // Create root VOB GOs.
            var allTypes = (VirtualObjectType[])Enum.GetValues(typeof(VirtualObjectType));
            foreach (var type in allTypes)
            {
                var newGo = new GameObject(type.ToString());
                newGo.SetParent(rootGo);

                _vobTypeParentGOs[type] = newGo;
            }
            
            PreLoadVobs(vobs);
        }

        private void PreLoadVobs(List<IVirtualObject> vobs)
        {
            foreach (var vob in vobs)
            {
                PreLoadVob(vob);
                PreLoadVobs(vob.Children);
            }
        }
        
        /// <summary>
        /// Some elements change when the game loads them for the first time. We change these values here.
        /// </summary>
        private void PreLoadVob(IVirtualObject vob)
        {
            if (!_saveGameService.IsWorldEnteredFirstTime)
                return;

            switch (vob.Type)
            {
                case VirtualObjectType.zCVobSound:
                    vob.ShowVisual = false; // Always 0 in G1 save games.
                    break;
                case VirtualObjectType.zCVobLight:
                    vob.ShowVisual = true; // Always 1 in G1 save games.
                    break;
            }

            vob.PresetName = string.Empty; // Never set in any G1 save game.
        }

        /// <summary>
        /// Hint: The calculation is somewhat off: When we set a VOB to be lazy loaded, we will not calculate its children as "created".
        ///       Therefore, the loading bar will "hop" at the end, as we didn't calculate correctly. But it causes no harm.
        /// </summary>
        private int GetTotalVobCount(List<IVirtualObject> vobs)
        {
            return vobs.Count + vobs.Sum(vob => GetTotalVobCount(vob.Children));
        }

        private void PostCreateWorldVobs()
        {
            // DEBUG - If we want to load all VOBs at once, we need to initialize all LazyLoad objects now.
            if (!_configService.Dev.EnableVOBMeshCulling)
            {
                var lazyLoadVobs = Object.FindObjectsOfType<VobLoader>(true);
                lazyLoadVobs.ForEach(i => InitVob(i.gameObject));
            }
        }

        private async Task CreateWorldVobs(DeveloperConfig config, LoadingService loading, List<IVirtualObject> vobs)
        {
            foreach (var vob in vobs)
            {
                // It's simpler to have both of them in here.
                loading.Tick();
                await _frameSkipperService.TrySkipToNextFrame();
                
                switch (vob.Type)
                {
                    // A LevelCompo contains no data. Simply check its children.
                    case VirtualObjectType.zCVobLevelCompo:
                        await CreateWorldVobs(config, loading, vob.Children);
                        continue;
                    case VirtualObjectType.oCNpc:
                        GlobalEventDispatcher.CreateNpc.Invoke((INpc)vob);
                        continue;
                }

                // If our VOB type is ignored by Dev config, skip it and its children.
                if (!config.SpawnVOBTypes.Value.IsEmpty() && !config.SpawnVOBTypes.Value.Contains(vob.Type))
                {
                    continue;
                }

                var container = CreateContainerWithLoader(vob);

                if (_vobTypesNonLazyLoading.Contains(vob.Type))
                {
                    CreateVobNow(container);
                    AddToMobInteractableList(container);
                }
                else
                {
                    CreateVobLazily(container);

                    // We assume that all VOBs with meshes are lazy loaded only.
                    AddToMobInteractableList(container);
                }
            }
        }

        private VobContainer CreateContainerWithLoader(IVirtualObject vob)
        {
            var container = new VobContainer(vob);
            _multiTypeCacheService.VobCache.Add(container);

            container.Go = new GameObject($"{container.Vob.GetVisualName()} (Loader)");
            var loader = container.Go.AddComponent<VobLoader>();
            loader.Container = container;

            _initializerDomain.SetPosAndRot(container.Go, container.Vob.Position, container.Vob.Rotation);
            container.Go.SetParent(GetRootGameObjectOfType(container.Vob.Type));

            return container;
        }

        /// <summary>
        /// Eager loading a VOB simply means we call the Init() method immediately.
        /// </summary>
        private void CreateVobNow(VobContainer container)
        {
            container.Go.GetComponent<VobLoader>().IsLoaded = true;
            InitVobNow(container);
        }

        public VobContainer CreateItem(IItem item)
        {
            var container = CreateContainerWithLoader(item);
            
            CreateVobNow(container);

            return container;
        }
        
        [CanBeNull]
        public VobContainer GetFreeInteractableWithin10M(Vector3 position, string visualScheme)
        {
            if (!_gameStateService.VobsInteractable.TryGetValue(visualScheme.ToUpper(), out var vobs))
                return null;
            
            return vobs
                .Where(pair => Vector3.Distance(pair.Vob.Position.ToUnityVector(), position) < _interactableLookupDistance)
                .OrderBy(pair => Vector3.Distance(pair.Vob.Position.ToUnityVector(), position))
                .FirstOrDefault();
        }
        public void ExtWldInsertItem(int itemInstance, string spawnPoint)
        {
            if (string.IsNullOrEmpty(spawnPoint) || itemInstance <= 0)
                return;

            var activeTypes = _configService.Dev.SpawnVOBTypes.Value;
            if (!_configService.Dev.EnableVOBs || (!activeTypes.IsEmpty() && activeTypes.Contains(VirtualObjectType.oCItem)))
                return;

            var item = _vmCacheService.TryGetItemData(itemInstance);
            var instanceName = _gameStateService.GothicVm.GetSymbolByIndex(item.Index)!.Name;
            var wp = _wayNetService.GetWayNetPoint(spawnPoint)!;

            var vob = new Item
            {
                Name = instanceName,
                Position = wp.Position.ToZkVector(),
                Rotation = wp.Rotation.ToZkMatrix(),
                Visual = new VisualMesh(),
                Instance = instanceName
            };

            var container = CreateContainerWithLoader(vob);
            _vobMeshCullingService.AddCullingEntry(container);
            _saveGameService.CurrentWorldData.Vobs.Add(container.Vob);
        }

        [CanBeNull]
        public GameObject GetNearestSlot(GameObject go, Vector3 position)
        {
            var goTransform = go.transform;

            if (goTransform.childCount == 0)
            {
                return null;
            }

            // We need to move into next elements starting from VobLoader root.
            var zm = go.transform.GetChild(0).GetChild(0);

            return zm.gameObject.GetAllDirectChildren()
                .Where(i => i.name.ContainsIgnoreCase("ZS"))
                .OrderBy(i => Vector3.Distance(i.transform.position, position))
                .FirstOrDefault();
        }
        
        /// <summary>
        /// When we Lazy Load a VOB, we add their culling information to load them later.
        /// Once Culling fetches the object, we will read this data and call InitVob() later.
        /// </summary>
        private void CreateVobLazily(VobContainer container)
        {
            // Skip disabled features.
            switch (container.Vob.Visual!.Type)
            {
                case VisualType.Decal:
                    // Skip object
                    if (!_configService.Dev.EnableDecalVisuals)
                        return;
                    break;
                case VisualType.ParticleEffect:
                    // Skip object
                    if (!_configService.Dev.EnableParticleEffects)
                        return;
                    break;
            }
            
            // Non-static lights aren't handled so far.
            if (container.Vob.Type == VirtualObjectType.zCVobLight && !((ILight)container.Vob).LightStatic)
            {
                return;
            }

            if (container.Vob.Type == VirtualObjectType.zCVobSound ||
                container.Vob.Type == VirtualObjectType.zCVobSoundDaytime)
            {
                _vobSoundCullingService.AddCullingEntry(container);
                return;
            }

            _vobMeshCullingService.AddCullingEntry(container);
        }

        private void AddToMobInteractableList(VobContainer container)
        {
            if (container.Go == null)
                return;

            switch (container.Vob.Type)
            {
                // case VirtualObjectType.oCMOB: // FIXME - Needed? e.g. IMovableObject
                // oCMobDoor excluded: doors use HVRPhysicsDoor for VR interaction, not AI_UseMob.
                case VirtualObjectType.oCMobFire:
                case VirtualObjectType.oCMobInter:
                case VirtualObjectType.oCMobBed:
                case VirtualObjectType.oCMobContainer:
                case VirtualObjectType.oCMobSwitch:
                case VirtualObjectType.oCMobWheel:
                    var visualScheme = container.Vob.Visual?.Name.Split('_').First().ToUpper(); // e.g. BED_1_OC.ASC => BED);

                    if (visualScheme.IsNullOrEmpty())
                        return;

                    _gameStateService.VobsInteractable.TryAdd(visualScheme, new());
                    _gameStateService.VobsInteractable[visualScheme!].Add(container);
                    break;

                case VirtualObjectType.zCMover:
                    var moverName = container.Vob.Name;
                    if (!string.IsNullOrEmpty(moverName))
                    {
                        var moverKey = moverName.ToUpper();
                        if (!_gameStateService.VobsMover.TryGetValue(moverKey, out var moverList))
                            _gameStateService.VobsMover[moverKey] = moverList = new();
                        moverList.Add(container);
                    }
                    break;
            }
        }

        public bool TryGetMovers(string name, out List<VobContainer> containers)
        {
            var start = 0;
            while (start < name.Length && !char.IsLetterOrDigit(name[start]))
                start++;
            var key = name.Substring(start).ToUpper();
            if (_gameStateService.VobsMover.TryGetValue(key, out containers)) return true;
            return _gameStateService.VobsMover.TryGetValue(key + ".3DS", out containers);
        }

        public bool TryGetMover(string name, out VobContainer container)
        {
            if (!TryGetMovers(name, out var list) || list.Count == 0) { container = null; return false; }
            container = list[0];
            return true;
        }

        public List<ContentItem> UnpackItems(string contents)
        {
            List<ContentItem> result = new();

            if (contents.IsNullOrEmpty())
                return new();
            
            var items = contents.Split(_itemNameSeparators);
        
            foreach (var item in items)
            {
                var count = 1;
                var nameCountSplit = item.Split(_itemCountSeparators);
        
                if (nameCountSplit.Length != 1)
                    count = int.Parse(nameCountSplit[1]);
        
                result.Add(new ContentItem(nameCountSplit[0], count));
            }

            return result;
        }

        public string PackItems(List<ContentItem> items)
        {
            return string.Join(';', items.Select(i => $"{i.Name}:{i.Amount}"));
        }
    }
}
