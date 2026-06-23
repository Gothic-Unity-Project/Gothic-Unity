using System;
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
using ZenKit.Util;
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
            VirtualObjectType.zCCodeMaster,
            VirtualObjectType.zCTrigger,
            VirtualObjectType.zCTriggerUntouch,
            VirtualObjectType.zCTriggerWorldStart,
            VirtualObjectType.zCMoverController,
            VirtualObjectType.zCMessageFilter,
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

            SpawnItemVob(instanceName, wp.Position.ToZkVector(), wp.Rotation.ToZkMatrix());
        }

        /// <summary>
        /// Spawns an item VOB at a given world position with physics — used for weapon drops on NPC death/knockout.
        /// </summary>
        public void DropItemAtPosition(int symbolIndex, Vector3 worldPosition)
        {
            var activeTypes = _configService.Dev.SpawnVOBTypes.Value;
            if (!_configService.Dev.EnableVOBs || (!activeTypes.IsEmpty() && activeTypes.Contains(VirtualObjectType.oCItem)))
                return;

            var sym = _gameStateService.GothicVm.GetSymbolByIndex(symbolIndex);
            if (sym == null)
            {
                Logger.LogWarning($"[VobService] DropItemAtPosition: no symbol at index {symbolIndex}", LogCat.Vob);
                return;
            }

            Logger.Log($"[VobService] DropItemAtPosition: '{sym.Name}' at {worldPosition}", LogCat.Vob);

            var vob = new Item
            {
                Name = sym.Name,
                Position = worldPosition.ToZkVector(),
                Rotation = Quaternion.identity.ToZkMatrix(),
                Visual = new VisualMesh(),
                Instance = sym.Name
            };

            var container = CreateContainerWithLoader(vob);
            CreateVobNow(container);
            _saveGameService.CurrentWorldData.Vobs.Add(container.Vob);

            // Add physics so the item falls to the ground.
            // The VobItemWeapon prefab only has trigger colliders (for HVR grab); we add a solid
            // BoxCollider so the Rigidbody has something to rest on world geometry.
            if (container.Go != null)
            {
                var col = container.Go.AddComponent<BoxCollider>();
                col.size = new Vector3(0.1f, 0.06f, 0.5f);
                col.center = new Vector3(0f, 0.03f, 0f);

                var rb = container.Go.AddComponent<Rigidbody>();
                rb.mass = 1f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
        }

        /// <summary>
        /// Finds the nearest world VobContainer for a given item symbol within maxDist meters.
        /// Uses actual GO position and skips kinematic (VR-grabbed) items.
        /// </summary>
        public VobContainer FindNearbyWorldItemContainer(string instanceName, Vector3 nearPosition, float maxDist = 5f)
        {
            VobContainer nearest = null;
            var minDist = maxDist;

            foreach (var container in _multiTypeCacheService.VobCache)
            {
                if (container.Vob is not IItem vobItem) continue;
                if (container.Go == null) continue;

                // Skip items grabbed by VR hands — HVR sets Rigidbody.isKinematic=true on grab
                var rb = container.Go.GetComponent<Rigidbody>();
                if (rb != null && rb.isKinematic) continue;

                var itemSym = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
                if (!string.Equals(itemSym, instanceName, StringComparison.OrdinalIgnoreCase)) continue;

                var dist = Vector3.Distance(container.Go.transform.position, nearPosition);
                if (dist >= minDist) continue;

                minDist = dist;
                nearest = container;
            }

            return nearest;
        }

        /// <summary>
        /// Removes a VobContainer from the world cache without destroying its GameObject.
        /// Use when the player physically takes a world item (GO stays alive in VR hands).
        /// </summary>
        public void UntrackVobFromCache(VobContainer container)
        {
            if (!_multiTypeCacheService.VobCache.Remove(container))
                return;
            _saveGameService.CurrentWorldData.Vobs.Remove(container.Vob);
            Logger.Log($"[VobService] UntrackVobFromCache: '{container.Vob.Name}' removed from cache", LogCat.Vob);
        }

        public void RemoveWorldItem(string instanceName, Vector3 nearPosition)
        {
            VobContainer toRemove = null;
            var minDist = float.MaxValue;

            foreach (var container in _multiTypeCacheService.VobCache)
            {
                if (container.Vob is not IItem vobItem) continue;
                if (container.Go == null) continue;

                // Skip items held in VR hands (kinematic = grabbed)
                var rb = container.Go.GetComponent<Rigidbody>();
                if (rb != null && rb.isKinematic) continue;

                var itemSym = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
                if (!string.Equals(itemSym, instanceName, StringComparison.OrdinalIgnoreCase)) continue;

                // Use actual GO position (physics may have moved it from spawn point)
                var dist = Vector3.Distance(container.Go.transform.position, nearPosition);
                if (dist >= minDist) continue;

                minDist = dist;
                toRemove = container;
            }

            if (toRemove == null)
            {
                Logger.LogWarning($"[VobService] RemoveWorldItem: '{instanceName}' not found near {nearPosition}", LogCat.Vob);
                return;
            }

            Logger.Log($"[VobService] RemoveWorldItem: destroying '{instanceName}' GO dist={minDist:F1}m from NPC", LogCat.Vob);
            _multiTypeCacheService.VobCache.Remove(toRemove);
            _saveGameService.CurrentWorldData.Vobs.Remove(toRemove.Vob);
            Object.Destroy(toRemove.Go);
        }

        private void SpawnItemVob(string instanceName, System.Numerics.Vector3 position, Matrix3x3 rotation)
        {
            var vob = new Item
            {
                Name = instanceName,
                Position = position,
                Rotation = rotation,
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

                case VirtualObjectType.oCTriggerScript:
                    // Vob.Name may be empty; GO name = "{GetVisualName()} (Loader)" which matches mob.Target.
                    var triggerName = container.Go.name.Replace(" (Loader)", "");
                    if (!string.IsNullOrEmpty(triggerName))
                    {
                        _gameStateService.VobsTriggerScript[triggerName.ToUpper()] = container;
                        Logger.Log($"[VobService] Registered TriggerScript: '{triggerName}'", LogCat.Vob);
                    }
                    break;

                case VirtualObjectType.zCTriggerList:
                    var tlName = container.Vob.Name?.ToUpper();
                    if (!string.IsNullOrEmpty(tlName))
                    {
                        _gameStateService.VobsTriggerList[tlName] = container;
                        Logger.Log($"[VobService] Registered TriggerList: '{tlName}'", LogCat.Vob);
                    }
                    break;

                case VirtualObjectType.zCCodeMaster:
                    var cmName = container.Vob.Name?.ToUpper();
                    if (!string.IsNullOrEmpty(cmName))
                    {
                        _gameStateService.VobsCodeMaster[cmName] = container;
                        Logger.Log($"[VobService] Registered CodeMaster: '{cmName}'", LogCat.Vob);
                    }
                    break;

                case VirtualObjectType.zCTrigger:
                    var trigZoneName = container.Vob.Name?.ToUpper();
                    if (!string.IsNullOrEmpty(trigZoneName))
                    {
                        _gameStateService.VobsTrigger[trigZoneName] = container;
                        Logger.Log($"[VobService] Registered TriggerZone: '{trigZoneName}'", LogCat.Vob);
                    }
                    break;

                case VirtualObjectType.zCMoverController:
                    var mcName = container.Vob.Name?.ToUpper();
                    if (!string.IsNullOrEmpty(mcName))
                    {
                        _gameStateService.VobsMoverController[mcName] = container;
                        Logger.Log($"[VobService] Registered MoverController: '{mcName}'", LogCat.Vob);
                    }
                    break;

                case VirtualObjectType.zCMessageFilter:
                    var mfName = container.Vob.Name?.ToUpper();
                    if (!string.IsNullOrEmpty(mfName))
                    {
                        _gameStateService.VobsMessageFilter[mfName] = container;
                        Logger.Log($"[VobService] Registered MessageFilter: '{mfName}'", LogCat.Vob);
                    }
                    break;
            }
        }

        /// <summary>
        /// Fires a named trigger chain: resolves by mover → code master → trigger list → trigger script.
        /// Used by Wld_SendTrigger, chained movers, and mob-grab targets.
        /// senderName: name of the VOB that initiated the trigger (needed by CodeMaster slave matching).
        /// </summary>
        public void DispatchTrigger(string name, string senderName = "")
        {
            if (TryGetMovers(name, out var movers))
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' → mover(s) (Toggle)", LogCat.Vob);
                foreach (var c in movers)
                {
                    if (c?.Go == null || !c.Go) continue;
                    c.Go.GetComponentInChildren<MoverAdapter>()?.Toggle();
                }
                return;
            }

            var key = name.ToUpper();

            if (_gameStateService.VobsCodeMaster.TryGetValue(key, out var cm) && cm?.Go != null && cm.Go)
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' ← '{senderName}' → CodeMaster", LogCat.Vob);
                cm.Go.GetComponentInChildren<CodeMasterHandler>(true)?.ReceiveTrigger(senderName);
                return;
            }

            if (_gameStateService.VobsTriggerList.TryGetValue(key, out var tl) && tl?.Go != null && tl.Go)
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' → TriggerList", LogCat.Vob);
                tl.Go.GetComponentInChildren<TriggerListHandler>(true)?.Trigger();
                return;
            }

            // TriggerScript and MessageFilter can coexist under the same name — do NOT early-return after TriggerScript.
            var dispatched = false;

            if (_gameStateService.VobsTriggerScript.TryGetValue(key, out var ts) && ts?.Go != null && ts.Go)
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' → TriggerScript", LogCat.Vob);
                ts.Go.GetComponentInChildren<TriggerScriptHandler>(true)?.Trigger();
                dispatched = true;
            }

            if (_gameStateService.VobsMessageFilter.TryGetValue(key, out var mf) && mf?.Go != null && mf.Go)
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' → MessageFilter", LogCat.Vob);
                mf.Go.GetComponentInChildren<MessageFilterHandler>(true)?.Trigger();
                dispatched = true;
            }

            if (_gameStateService.VobsTrigger.TryGetValue(key, out var tz) && tz?.Go != null && tz.Go)
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' → TriggerZone (OnTrigger)", LogCat.Vob);
                tz.Go.GetComponentInChildren<TriggerZoneHandler>(true)?.ReceiveTrigger();
                dispatched = true;
            }

            if (_gameStateService.VobsMoverController.TryGetValue(key, out var mc) && mc?.Go != null && mc.Go)
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' → MoverController", LogCat.Vob);
                mc.Go.GetComponentInChildren<MoverControllerHandler>(true)?.Trigger();
                dispatched = true;
            }

            if (!dispatched)
                Logger.LogWarning($"[VobService] DispatchTrigger: '{name}' not found in movers, code masters, trigger lists, trigger scripts, trigger zones, message filters, or mover controllers", LogCat.Vob);
        }

        /// <summary>
        /// Fires an untrigger event to the named VOB. Currently handled by CodeMaster (UntriggeredCancels).
        /// senderName: VOB that sent the untrigger (for CodeMaster slave matching).
        /// </summary>
        public void DispatchUntrigger(string name, string senderName = "")
        {
            if (TryGetMovers(name, out var movers))
            {
                Logger.Log($"[VobService] DispatchUntrigger '{name}' → mover(s) (Close)", LogCat.Vob);
                foreach (var c in movers)
                {
                    if (c?.Go == null || !c.Go) continue;
                    c.Go.GetComponentInChildren<MoverAdapter>()?.Close();
                }
                return;
            }

            var key = name.ToUpper();

            if (_gameStateService.VobsCodeMaster.TryGetValue(key, out var cm) && cm?.Go != null && cm.Go)
            {
                Logger.Log($"[VobService] DispatchUntrigger '{name}' ← '{senderName}' → CodeMaster", LogCat.Vob);
                cm.Go.GetComponentInChildren<CodeMasterHandler>(true)?.ReceiveUntrigger(senderName);
                return;
            }

            Logger.LogWarning($"[VobService] DispatchUntrigger: '{name}' — no handler found", LogCat.Vob);
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
