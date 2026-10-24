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

        /// <summary>
        /// VOBs culled in but not created yet (InitVobCoroutine works them off in batches after the world is entered).
        /// </summary>
        public bool HasPendingVobInits => _objectsToInitQueue.Count > 0;
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
                            if (item.IsTraced)
                                Logger.LogWarning($"[VobTrace] '{item.name}' initialized: {item.transform.childCount} " +
                                                  $"children, {item.GetComponentsInChildren<Renderer>(true).Length} " +
                                                  $"renderers", LogCat.Vob);
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
        public GameObject CreateItemMesh(int itemId, GameObject parentGo)
        {
            if (itemId == -1)
            {
                Logger.LogError("No ItemId found. Is this a bug on daedalus or our side?", LogCat.Vob);
                return null; // no item
            }
            var item = _vmCacheService.TryGetItemData(itemId);

            return _initializerDomain.CreateItemMesh(item, parentGo, default);
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

            LogMobOwners();
        }

        /// <summary>
        /// Diagnostics for PERC_ASSESSUSEMOB: how many mobs have an owner / owner guild (G1: Old Camp chests GIL_GRD).
        /// </summary>
        private void LogMobOwners()
        {
            var withOwner = 0;
            var samples = new List<string>();

            void Walk(List<IVirtualObject> vobs)
            {
                foreach (var vob in vobs)
                {
                    if (vob is IMovableObject mob && (!string.IsNullOrEmpty(mob.Owner) || !string.IsNullOrEmpty(mob.OwnerGuild)))
                    {
                        withOwner++;
                        if (samples.Count < 6)
                            samples.Add($"{vob.Name}/{vob.Visual?.Name}={mob.Owner}|{mob.OwnerGuild}");
                    }
                    Walk(vob.Children);
                }
            }

            try
            {
                Walk(_saveGameService.CurrentWorldData.Vobs);
                Logger.Log($"[Mobs] {withOwner} mobs with an owner; e.g. {string.Join(", ", samples)}", LogCat.Vob);
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"[Mobs] Owner check failed: {e.Message}", LogCat.Vob);
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

                // DeveloperConfig.EnableEmptyVobContainerChildren: an unnamed zCVob without a visual only groups its
                // children - like a LevelCompo. As a lazy loader it never loaded (no visual = no bounds = never
                // "visible"), and nothing below it ever appeared: The Chronicles Of Myrtana's intro ship and ~8000
                // other VOBs. Its children become loaders of their own (VOB positions are world positions).
                if (config.EnableEmptyVobContainerChildren && IsEmptyVobContainer(vob))
                {
                    await CreateWorldVobs(config, loading, vob.Children);
                    continue;
                }
                // Decal/particle VOBs are skipped while those visuals are off (EnableDecalVisuals/EnableParticleEffects,
                // both off in Production) - and their children went with them: G2's treasure X marks (children of the
                // RAKEPLACE decal) never appeared in builds, only in the Editor with a config that has decals on. The
                // children become loaders of their own, the decal/particle itself stays skipped.
                if (config.EnableEmptyVobContainerChildren && vob.Children.Count > 0 && IsVisualDisabled(config, vob))
                {
                    await CreateWorldVobs(config, loading, vob.Children);
                    continue;
                }

                // If our VOB type is ignored by Dev config, skip it and its children.
                if (!config.SpawnVOBTypes.Value.IsEmpty() && !config.SpawnVOBTypes.Value.Contains(vob.Type))
                {
                    continue;
                }

                var container = CreateContainerWithLoader(vob);

                // DeveloperConfig.EnableChildLogicVobs: cutscene cameras, cutscene triggers, fog zones, ... aren't built
                // (no visual - never lazy loaded, and InitVob skips their children). Their children are: G2 Irdorath's
                // camera VOBs carry the trigger lists that move the pillars/gates ("EVT_LEFT_UP_CAMERA_01_TRIGGERLIST"
                // ... not found). The children become loaders of their own (VOB positions are world positions).
                if (config.EnableChildLogicVobs && vob.Children.Count > 0 && IsUnbuiltVobType(vob.Type))
                    await CreateWorldVobs(config, loading, vob.Children);

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
        /// <summary>
        /// VOB types InitVob only creates an empty placeholder for - without their children.
        /// </summary>
        public static bool IsUnbuiltVobType(VirtualObjectType type)
        {
            return type is VirtualObjectType.zCVobScreenFX or VirtualObjectType.oCCSTrigger or
                VirtualObjectType.zCVobLensFlare or VirtualObjectType.zCZoneZFog or VirtualObjectType.zCZoneZFogDefault or
                VirtualObjectType.zCZoneVobFarPlane or VirtualObjectType.zCZoneVobFarPlaneDefault or
                VirtualObjectType.zCCSCamera or VirtualObjectType.zCCamTrj_KeyFrame or VirtualObjectType.oCTouchDamage or
                VirtualObjectType.zCEarthquake;
        }

        private static bool IsVisualDisabled(DeveloperConfig config, IVirtualObject vob)
        {
            return vob.Visual?.Type switch
            {
                VisualType.Decal => !config.EnableDecalVisuals,
                VisualType.ParticleEffect => !config.EnableParticleEffects,
                _ => false
            };
        }


        private static bool IsEmptyVobContainer(IVirtualObject vob)
        {
            return vob.Type == VirtualObjectType.zCVob && vob.Children.Count > 0 && string.IsNullOrEmpty(vob.Name) &&
                   (vob.Visual == null || string.IsNullOrEmpty(vob.Visual.Name));
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

            if (IsTraced(container.Vob))
            {
                loader.IsTraced = true;
                Logger.LogWarning($"[VobTrace] loader '{container.Go.name}' ({container.Vob.Type}, " +
                                  $"'{container.Vob.Name}') created at {container.Go.transform.position}, " +
                                  $"showVisual={container.Vob.ShowVisual}", LogCat.Vob);
            }

            return container;
        }

        /// <summary>
        /// DeveloperConfig.DebugTraceVobVisual: follow the VOBs whose visual contains this text (e.g. a mod's model
        /// that never shows).
        /// </summary>
        private bool IsTraced(IVirtualObject vob)
        {
            var trace = _configService.Dev.DebugTraceVobVisual;
            return !string.IsNullOrEmpty(trace) &&
                   vob.GetVisualName().IndexOf(trace, StringComparison.OrdinalIgnoreCase) >= 0;
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
        /// DeveloperConfig.EnableMobOccupancy (with user): like the engine, a mob whose slots (ZS_*) are all used by
        /// other NPCs isn't free - two miners spawned next to each other hacked at the same ore vein. A bench with two
        /// slots takes two.
        /// </summary>
        [CanBeNull]
        public VobContainer GetFreeInteractableWithin10M(Vector3 position, string visualScheme,
            NpcContainer user = null)
        {
            if (!_gameStateService.VobsInteractable.TryGetValue(visualScheme.ToUpper(), out var vobs))
                return null;

            var candidates = vobs
                .Where(pair => Vector3.Distance(pair.Vob.Position.ToUnityVector(), position) < _interactableLookupDistance)
                .OrderBy(pair => Vector3.Distance(pair.Vob.Position.ToUnityVector(), position));

            if (user == null || !_configService.Dev.EnableMobOccupancy)
                return candidates.FirstOrDefault();

            foreach (var mob in candidates)
            {
                if (GetOtherMobUsers(mob, user).Count() < GetSlotCount(mob))
                    return mob;
            }

            Logger.Log($"[VobService] No free '{visualScheme}' within {_interactableLookupDistance} m for " +
                       $"{user.Instance?.GetName(ZenKit.Daedalus.NpcNameSlot.Slot0)}", LogCat.Vob);
            return null;
        }

        /// <summary>
        /// Other NPCs using this mob (in one of its states) or walking to it - a reservation left by an interrupted
        /// walk doesn't block it.
        /// </summary>
        public IEnumerable<NpcContainer> GetOtherMobUsers(VobContainer mob, NpcContainer user)
        {
            return _multiTypeCacheService.NpcCache.Where(i =>
                i != null && i != user && i.PrefabProps != null && i.PrefabProps.CurrentInteractable == mob &&
                (i.Props.CurrentInteractableStateId >= 0 ||
                 i.Props.CurrentAction is Gothic.Core.Domain.Npc.Actions.AnimationActions.UseMob));
        }

        private int GetSlotCount(VobContainer mob)
        {
            var slots = GetSlots(mob.Go);
            // Not loaded yet or no slot objects: one user, like a mob with a single position.
            return slots.Count > 0 ? slots.Count : 1;
        }

        private List<GameObject> GetSlots([CanBeNull] GameObject go)
        {
            if (go == null || go.transform.childCount == 0 || go.transform.GetChild(0).childCount == 0)
                return new List<GameObject>();

            // We need to move into next elements starting from VobLoader root.
            var zm = go.transform.GetChild(0).GetChild(0);
            return zm.gameObject.GetAllDirectChildren()
                .Where(i => i.name.ContainsIgnoreCase("ZS"))
                .ToList();
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

        // Collides with the world only (DynamicsManager: TransparentFX - Default, not Player/Hand/VobHitbox).
        private static readonly int _dropColliderLayer = LayerMask.NameToLayer("TransparentFX");

        /// <summary>
        /// Spawns an item VOB at a given world position with physics — used for weapon drops on NPC death/knockout.
        /// </summary>
        public VobContainer DropItemAtPosition(int symbolIndex, Vector3 worldPosition)
        {
            var activeTypes = _configService.Dev.SpawnVOBTypes.Value;
            if (!_configService.Dev.EnableVOBs || (!activeTypes.IsEmpty() && activeTypes.Contains(VirtualObjectType.oCItem)))
                return null;

            var sym = _gameStateService.GothicVm.GetSymbolByIndex(symbolIndex);
            if (sym == null)
            {
                Logger.LogWarning($"[VobService] DropItemAtPosition: no symbol at index {symbolIndex}", LogCat.Vob);
                return null;
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
            // It's a regular world item from now on. Without a culling entry, grabbing it logs
            // "Couldn't find object in Culling list" and its position updates aren't tracked.
            _vobMeshCullingService.AddCullingEntry(container);
            _saveGameService.CurrentWorldData.Vobs.Add(container.Vob);

            // Add a solid BoxCollider so the existing Rigidbody has something to rest on world geometry.
            // IMPORTANT: do NOT add a second Rigidbody — the oCItem prefab already has one (kinematic by
            // default). VrWeaponAttackDomain.TryHandle() does GetComponentInChildren<Rigidbody>() to track
            // weapon velocity; a second root Rigidbody would shadow the prefab's one and break hit detection
            // for weapons picked directly from the ground.
            if (container.Go != null)
            {
                // DeveloperConfig.EnableDroppedItemColliderLayer: on a child on TransparentFX - it rests on the world
                // (Default) but neither blocks the hero (Player, Hand) nor stops arrows/bolts. On the loader (Default) an
                // archer's missed bolts lay in the ground in front of the hero, blocked his way and the next bolts hit
                // them - they piled up in one spot. Grabbing uses the item mesh's own colliders.
                var colliderGo = container.Go;
                if (_configService.Dev.EnableDroppedItemColliderLayer)
                {
                    colliderGo = new GameObject("DropCollider") { layer = _dropColliderLayer };
                    colliderGo.transform.SetParent(container.Go.transform, false);
                }
                var col = colliderGo.AddComponent<BoxCollider>();
                col.size = new Vector3(0.1f, 0.06f, 0.5f);
                col.center = new Vector3(0f, 0.03f, 0f);

                var rb = container.Go.GetComponentInChildren<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.mass = 1f;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                }
            }

            return container;
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
                if (container.IsHeldByPlayer) continue;

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

        /// <summary>
        /// Removes exactly this world item (cache + save data) and destroys its GameObject.
        /// Callers must pass the container they validated themselves - re-searching by name here could pick
        /// another copy of the item (e.g., the one in the player's hand).
        /// </summary>
        public void RemoveWorldItem(VobContainer container)
        {
            Logger.Log($"[VobService] RemoveWorldItem: destroying '{container.Vob.Name}' GO", LogCat.Vob);
            _vobMeshCullingService.RemoveCullingEntry(container);
            _multiTypeCacheService.VobCache.Remove(container);
            _saveGameService.CurrentWorldData.Vobs.Remove(container.Vob);
            Object.Destroy(container.Go);
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

        /// <summary>
        /// The nearest slot not taken by another NPC (DeveloperConfig.EnableMobOccupancy).
        /// </summary>
        [CanBeNull]
        public GameObject GetNearestFreeSlot(VobContainer mob, Vector3 position, NpcContainer user)
        {
            var taken = GetOtherMobUsers(mob, user)
                .Select(i => i.PrefabProps.CurrentInteractableSlot)
                .Where(i => i != null)
                .ToHashSet();

            return GetSlots(mob.Go)
                .Where(i => !taken.Contains(i))
                .OrderBy(i => Vector3.Distance(i.transform.position, position))
                .FirstOrDefault();
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
            
            // DeveloperConfig.EnableDynamicVobLights: non-static lights shine at runtime like in the engine (switched off
            // ones stay off). Before they were skipped - Mroczne Tajemnice's abandoned mine is lit only by such lights
            // (blue/teal AURA lights, no lightmaps, no vertex light) and stayed pitch black.
            if (container.Vob.Type == VirtualObjectType.zCVobLight && !((ILight)container.Vob).LightStatic &&
                (!_configService.Dev.EnableDynamicVobLights || !((ILight)container.Vob).On))
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

            // See GameStateService.SuppressVobTriggerDispatch: set while RebindNpcAliases() re-runs
            // INIT_ on save-game load, when VOB GameObjects exist but haven't Awake()/DI-injected yet
            // (still parented under the disabled vobRoot) - dispatching here would NRE on null
            // [Inject] fields such as TriggerScriptHandler's VmService.
            if (_gameStateService.SuppressVobTriggerDispatch)
            {
                Logger.Log($"[VobService] DispatchTrigger '{name}' suppressed (NPC alias rebind in progress)",
                    LogCat.Vob);
                return;
            }

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
        /// DeveloperConfig.EnableTriggerEnableState: zCMessageFilter Enable/Disable/Toggle on the named VOB. Only movers
        /// for now (G2 Irdorath's large gates are disabled once they opened, the next lever round moves the small ones).
        /// False if not handled.
        /// </summary>
        public bool DispatchEnable(string name, MessageFilterAction action)
        {
            if (!_configService.Dev.EnableTriggerEnableState || !TryGetMovers(name, out var movers))
                return false;

            var isHandled = false;
            foreach (var container in movers)
            {
                if (container?.Go == null || !container.Go)
                    continue;
                var mover = container.Go.GetComponentInChildren<MoverAdapter>();
                if (mover == null)
                    continue;
                mover.SetEnabled(action);
                isHandled = true;
            }
            return isHandled;
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
        

        public bool TryGetMover(string name, out VobContainer container)
        {
            if (!TryGetMovers(name, out var list) || list.Count == 0)
            {
                container = null;
                return false;
            }
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
