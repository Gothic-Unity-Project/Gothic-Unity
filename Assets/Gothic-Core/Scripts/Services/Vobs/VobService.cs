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
        


        private const int VobInitBatchSize = 10;

        private Dictionary<VirtualObjectType, GameObject> _vobTypeParentGOs = new();
        private Queue<VobLoader> _objectsToInitQueue = new();

        private int _dispatchTriggerDepth;
        private const int _maxDispatchTriggerDepth = 8;

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

        /// <summary>
        /// Name lookups of the previous world must not survive a world change: called on LoadGameStart (new game,
        /// loading a save) and by BootstrapService for level changes (oCTriggerChangeLevel).
        /// </summary>
        public void PreWorldCreate()
        {
            _gameStateService.VobsInteractable.Clear();
            _gameStateService.ClearTriggerVobs();
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

        /// <summary>
        /// An item outside the world list, e.g. for inventory pages, holsters and chest slots.
        /// </summary>
        public VobContainer CreateItem(string itemName, int amount)
        {
            return CreateItem(new Item
            {
                Name = itemName,
                Visual = new VisualMesh(),
                Instance = itemName,
                Amount = amount
            });
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

            // Items of a restored world exist already (INIT_* scripts run again).
            if (_saveGameService.IsRunningInitScriptsOfRestoredWorld)
                return;

            var activeTypes = _configService.Dev.SpawnVOBTypes.Value;
            if (!_configService.Dev.EnableVOBs || (!activeTypes.IsEmpty() && activeTypes.Contains(VirtualObjectType.oCItem)))
                return;

            var item = _vmCacheService.TryGetItemData(itemInstance);
            var instanceName = _gameStateService.GothicVm.GetSymbolByIndex(item.Index)!.Name;
            var wp = _wayNetService.GetWayNetPoint(spawnPoint);

            // An exception would abort the calling STARTUP_* script (e.g. G2's STARTUP_OLDWORLD).
            if (wp == null)
            {
                Logger.LogWarning($"Wld_InsertItem: Waypoint {spawnPoint} for {instanceName} doesn't exist. Skipping.",
                    LogCat.Vob);
                return;
            }

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
            _saveGameService.AddVobToWorld(container.Vob);
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
            var root = goTransform.GetChild(0);
            if (root.childCount == 0)
                return null;

            var zm = root.GetChild(0);

            // Plain loop instead of LINQ: routine loops call this through Wld_IsMobAvailable very often.
            GameObject nearest = null;
            var nearestSqrDistance = float.MaxValue;
            for (var i = 0; i < zm.childCount; i++)
            {
                var child = zm.GetChild(i);
                if (!child.name.ContainsIgnoreCase("ZS"))
                    continue;

                var sqrDistance = (child.position - position).sqrMagnitude;
                if (sqrDistance >= nearestSqrDistance)
                    continue;

                nearest = child.gameObject;
                nearestSqrDistance = sqrDistance;
            }

            return nearest;
        }

        /// <summary>
        /// Like Gothic, a mob needs a spot to be used from. A mob not built yet counts as usable and is checked on use.
        /// </summary>
        public bool IsMobUsableFrom(VobContainer mob, Vector3 position)
        {
            if (!mob.Go || !mob.Go.TryGetComponent<VobLoader>(out var loader))
            {
                Logger.LogWarning($"Mob {mob.Vob.Name} has no GameObject or VobLoader. Treating it as unusable.",
                    LogCat.Vob);
                return false;
            }

            return !loader.IsLoaded || GetNearestSlot(mob.Go, position) != null;
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
                // oCMobDoor excluded: doors use VR hinge physics for interaction, not AI_UseMob.
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
                case VirtualObjectType.oCTriggerScript:
                case VirtualObjectType.zCTriggerList:
                case VirtualObjectType.zCCodeMaster:
                case VirtualObjectType.zCTrigger:
                case VirtualObjectType.zCMoverController:
                case VirtualObjectType.zCMessageFilter:
                    _gameStateService.RegisterNamedVob(container);
                    break;
            }
        }

        /// <summary>
        /// Fires a named trigger chain: resolves by mover -> code master -> trigger list -> trigger script
        /// (+ message filter, trigger zone and mover controller of the same name).
        /// Used by Wld_SendTrigger, trigger VOBs and mob-grab targets.
        /// senderName: name of the VOB that initiated the trigger (needed by CodeMaster slave matching).
        /// </summary>
        public void DispatchTrigger(string name, string senderName = "")
        {
            if (string.IsNullOrEmpty(name))
                return;

            // Gothic's engine routes trigger messages through an event manager queue (with optional
            // fireDelay), so a script re-triggering itself is a common timer pattern - it re-fires on
            // a later tick. We dispatch synchronously instead, which turns such a cycle into infinite
            // recursion in a single frame (G2 Renovation's INIT triggers crashed the editor this way).
            // Legit vanilla chains (TriggerList -> movers, chained mover targets) are wide, not deep,
            // so a small depth cap only ever cuts true cycles.
            if (_dispatchTriggerDepth >= _maxDispatchTriggerDepth)
            {
                Logger.LogWarning($"[VobService] DispatchTrigger '{name}' dropped - trigger chain deeper than " +
                                  $"{_maxDispatchTriggerDepth} levels (self-retriggering loop?)", LogCat.Vob);
                return;
            }

            _dispatchTriggerDepth++;
            try
            {
                DispatchTriggerInternal(name, senderName);
            }
            finally
            {
                _dispatchTriggerDepth--;
            }
        }

        private void DispatchTriggerInternal(string name, string senderName)
        {
            if (TryGetMovers(name, out var movers))
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' -> mover(s) (OnTrigger)", LogCat.Vob);
                foreach (var mover in movers)
                {
                    if (mover?.Go == null || !mover.Go)
                        continue;
                    mover.Go.GetComponentInChildren<MoverAdapter>()?.OnTrigger();
                }
                return;
            }

            var key = name.ToUpper();

            if (TryGetTriggerVob(_gameStateService.VobsCodeMaster, key, out var codeMaster))
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' from '{senderName}' -> CodeMaster", LogCat.Vob);
                codeMaster.Go.GetComponentInChildren<CodeMasterHandler>(true)?.ReceiveTrigger(senderName);
                return;
            }

            if (TryGetTriggerVob(_gameStateService.VobsTriggerList, key, out var triggerList))
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' -> TriggerList", LogCat.Vob);
                triggerList.Go.GetComponentInChildren<TriggerListHandler>(true)?.Trigger();
                return;
            }

            // TriggerScript, MessageFilter, TriggerZone and MoverController can share a name - all of them react.
            var isDispatched = false;

            if (TryGetTriggerVob(_gameStateService.VobsTriggerScript, key, out var triggerScript))
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' -> TriggerScript", LogCat.Vob);
                triggerScript.Go.GetComponentInChildren<TriggerScriptHandler>(true)?.Trigger();
                isDispatched = true;
            }

            if (TryGetTriggerVob(_gameStateService.VobsMessageFilter, key, out var messageFilter))
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' -> MessageFilter", LogCat.Vob);
                messageFilter.Go.GetComponentInChildren<MessageFilterHandler>(true)?.Trigger();
                isDispatched = true;
            }

            if (TryGetTriggerVob(_gameStateService.VobsTrigger, key, out var triggerZone))
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' -> TriggerZone (OnTrigger)", LogCat.Vob);
                triggerZone.Go.GetComponentInChildren<TriggerZoneHandler>(true)?.ReceiveTrigger();
                isDispatched = true;
            }

            if (TryGetTriggerVob(_gameStateService.VobsMoverController, key, out var moverController))
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' -> MoverController", LogCat.Vob);
                moverController.Go.GetComponentInChildren<MoverControllerHandler>(true)?.Trigger();
                isDispatched = true;
            }

            if (!isDispatched)
                Logger.LogWarning($"[VobService] DispatchTrigger: no mover or trigger VOB named '{name}'", LogCat.Vob);
        }

        /// <summary>
        /// Fires an untrigger event to the named VOB: trigger-controlled movers close, a CodeMaster can reset its
        /// sequence, a MessageFilter runs its OnUntrigger action.
        /// senderName: VOB that sent the untrigger (for CodeMaster slave matching).
        /// </summary>
        public void DispatchUntrigger(string name, string senderName = "")
        {
            if (string.IsNullOrEmpty(name))
                return;

            if (TryGetMovers(name, out var movers))
            {
                Logger.Log($"[VobService] DispatchUntrigger '{name}' -> mover(s) (OnUntrigger)", LogCat.Vob);
                foreach (var mover in movers)
                {
                    if (mover?.Go == null || !mover.Go)
                        continue;
                    mover.Go.GetComponentInChildren<MoverAdapter>()?.OnUntrigger();
                }
                return;
            }

            var key = name.ToUpper();

            if (TryGetTriggerVob(_gameStateService.VobsCodeMaster, key, out var codeMaster))
            {
                Logger.Log($"[VobService] DispatchUntrigger '{name}' from '{senderName}' -> CodeMaster", LogCat.Vob);
                codeMaster.Go.GetComponentInChildren<CodeMasterHandler>(true)?.ReceiveUntrigger(senderName);
                return;
            }

            if (TryGetTriggerVob(_gameStateService.VobsMessageFilter, key, out var messageFilter))
            {
                Logger.Log($"[VobService] DispatchUntrigger '{name}' -> MessageFilter", LogCat.Vob);
                messageFilter.Go.GetComponentInChildren<MessageFilterHandler>(true)?.Untrigger();
                return;
            }

            Logger.LogWarning($"[VobService] DispatchUntrigger: '{name}' - no handler found", LogCat.Vob);
        }

        private static bool TryGetTriggerVob(Dictionary<string, VobContainer> registry, string key,
            out VobContainer container)
        {
            return registry.TryGetValue(key, out container) && container?.Go != null && container.Go;
        }

        /// <summary>
        /// All movers with this name (copy-pasted movers share one, and a trigger moves all of them).
        /// Mob and trigger targets can carry a "//" path prefix or miss the ".3DS" suffix of mesh movers.
        /// </summary>
        public bool TryGetMovers(string name, out List<VobContainer> containers)
        {
            if (string.IsNullOrEmpty(name))
            {
                containers = null;
                return false;
            }

            var start = 0;
            while (start < name.Length && !char.IsLetterOrDigit(name[start]))
                start++;
            var key = name.Substring(start).ToUpper();
            if (_gameStateService.VobsMover.TryGetValue(key, out containers))
                return true;
            return _gameStateService.VobsMover.TryGetValue(key + ".3DS", out containers);
        }
    }
}
