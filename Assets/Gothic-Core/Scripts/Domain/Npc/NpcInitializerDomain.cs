using System.Collections.Generic;
using System.Threading.Tasks;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Adapters.UI.LoadingBars;
using Gothic.Core.Creator;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Caches;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Proxy;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob.WayNet;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using JetBrains.Annotations;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;
using WayPoint = Gothic.Core.Models.Vob.WayNet.WayPoint;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Wrapper for Initialization topics from NpcManager
    /// </summary>
    public class NpcInitializerDomain
    {
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly NpcRoutineService _npcRoutineService;
        [Inject] private readonly FrameSkipperService _frameSkipperService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly VmCacheService _vmCacheService;

        
        public GameObject RootGo;
        private readonly List<(NpcContainer npc, string spawnPoint)> _tmpWldInsertNpcData = new();
        private int _nextInstanceId;

        private DaedalusVm Vm => _gameStateService.GothicVm;

        public async Task InitNpcsNewGame(LoadingService loading)
        {
            _nextInstanceId = 0;
            _monsterIndex = 0;
            _monsterWaypointCount.Clear();
            NewRunDaedalus();
            await NewAddLazyLoading(loading);
            if (_configService.Dev.EnableSaveLoadSystem)
                _saveGameService.SaveNpcInitSnapshot(_multiTypeCacheService.NpcCache);
        }

        public async Task InitNpcsSaveGame(LoadingService loading)
        {
            var saveGameNpcs = _saveGameService.CurrentWorldData.Npcs;

            foreach (var vobNpc in saveGameNpcs)
            {
                // Update the progress bar and check if we need to wait for the next frame now (As some conditions skip -continue- end of loop and would skip check)
                loading.Tick();
                await _frameSkipperService.TrySkipToNextFrame();

                var npcContainer = AllocZkInstance(vobNpc);
                if (npcContainer == null) continue;
                SaveGameAddLazyLoadingAnywhere(npcContainer, vobNpc.ScriptWaypoint);
            }
        }

        /// <summary>
        /// Save-game NPC init: spawns ALL NPCs from UNITYNPCINIT.json (the new-game baseline), then
        /// overrides position and attributes for those present in UNITYSAVE.json (the dirty delta).
        /// NPCs absent from the dirty dict are placed at their original spawn waypoints.
        /// </summary>
        public async Task InitNpcsFromMergedSnapshots(LoadingService loading, List<NpcInitEntry> initList, Dictionary<string, NpcSaveEntry> dirtyDict)
        {
            _monsterIndex = 0;
            _monsterWaypointCount.Clear();
            loading.SetPhase(nameof(WorldLoadingBarHandler.ProgressType.Npc), initList.Count);

            foreach (var initEntry in initList)
            {
                loading.Tick();
                await _frameSkipperService.TrySkipToNextFrame();

                var container = AllocZkInstance(initEntry.SymbolIndex);
                if (container == null) continue;
                container.InstanceId = initEntry.InstanceId; // restore stable init ID

                var dirtyKey = initEntry.GoName;
                NpcSaveEntry dirty = null;
                var hasDirty = dirtyDict != null && !dirtyKey.IsNullOrEmpty() && dirtyDict.TryGetValue(dirtyKey, out dirty);
                Logger.Log($"InitNpcsFromMergedSnapshots: '{dirtyKey}' hasDirty={hasDirty}{(hasDirty ? $" dead={dirty.IsDead} hp={dirty.Attributes?[0]}" : "")}", LogCat.Loading);

                // Apply saved routine before InitZkInstance reads vob.CurrentRoutine
                if (hasDirty && !dirty.CurrentRoutine.IsNullOrEmpty())
                    container.Vob.CurrentRoutine = dirty.CurrentRoutine;

                var go = InitLazyLoadNpc(container);

                if (hasDirty)
                {
                    var pos = new Vector3(dirty.Position[0], dirty.Position[1], dirty.Position[2]);
                    var rot = new Quaternion(dirty.Rotation[0], dirty.Rotation[1], dirty.Rotation[2], dirty.Rotation[3]);
                    go.transform.SetPositionAndRotation(pos, rot);

                    if (dirty.Attributes != null)
                    {
                        var vob = container.Vob;
                        for (var i = 0; i < dirty.Attributes.Length && i < 8; i++)
                            vob.SetAttribute(i, dirty.Attributes[i]);
                    }

                    // Restore the FP the NPC held at save time so it reclaims its home post
                    // instead of racing against other NPCs for the nearest unlocked FP.
                    if (!dirty.CurrentFreePointName.IsNullOrEmpty()
                        && _gameStateService.FreePoints.TryGetValue(dirty.CurrentFreePointName, out var savedFp))
                    {
                        container.Props.CurrentFreePoint = savedFp;
                        savedFp.IsLocked = true;
                    }
                }
                else
                {
                    // Apply init-time attributes saved post-startup-scripts (e.g. dead NPCs have HP=0)
                    if (initEntry.Attributes != null)
                        for (var i = 0; i < initEntry.Attributes.Length && i < 8; i++)
                            container.Vob.SetAttribute(i, initEntry.Attributes[i]);

                    var spawnPoint = GetSpawnPoint(container, initEntry.WaypointName);
                    if (spawnPoint == null)
                    {
                        Logger.LogWarning($"InitNpcsFromMergedSnapshots: waypoint '{initEntry.WaypointName}' not found for {initEntry.NpcInstance} — skipping.", LogCat.Npc);
                        Object.Destroy(go);
                        continue;
                    }
                    if (spawnPoint.IsFreePoint())
                        container.Props.CurrentFreePoint = (FreePoint)spawnPoint;
                    else
                        container.Props.CurrentWayPoint = (WayPoint)spawnPoint;
                    go.transform.SetPositionAndRotation(spawnPoint.Position, spawnPoint.Rotation);
                }

                container.Props.CurrentLoopState = NpcProperties.LoopState.None;
                container.Vob.CurrentStateValid = false;
                container.Vob.NextStateValid = false;

                _npcMeshCullingService.AddCullingEntry(go);
            }

            loading.FinalizePhase();
        }

        public void InitNpcVobSaveGame(INpc vobNpc)
        {
            var npcContainer = AllocZkInstance(vobNpc);
            if (npcContainer == null) return;
            SaveGameAddLazyLoadingNearby(npcContainer, vobNpc);
        }

        /// <summary>
        /// Original Gothic uses this function to spawn an NPC instance into the world.
        /// We collect this data only and create NPCs/Monsters in chunks afterward.
        ///
        /// Nevertheless, we need to fill the NpcCache already, as there are the following statements inside Startup.d:
        ///     Wld_InsertNpc(GRD_282_Nek,"");
        ///     var C_NPC nek;
        ///     nek = Hlp_GetNpc(GRD_282_Nek);
        /// --> We need to provide the NpcInstance for Hlp_GetNpc() already during fill up time. Even if we don't have a working mesh etc.
        /// --> Otherwise we get a NPE.
        /// --> We will fill the NpcCache with proper values later.
        /// </summary>
        /// <summary>
        /// Spawns an NPC at runtime (during gameplay, not during world loading).
        /// Used by Wld_SpawnNpcRange (summon spells) and Marvin spawn cheats.
        /// </summary>
        public GameObject SpawnNpcRuntime(int npcIndex, Vector3 position, Quaternion rotation)
        {
            var container = AllocZkInstance(npcIndex);
            Vm.InitInstance(container.Instance);
            container.IsZkInstanceInitialized = true;

            var go = InitLazyLoadNpc(container);
            go.transform.SetPositionAndRotation(GetFreeAreaAtSpawnPoint(position), rotation);
            _npcMeshCullingService.AddCullingEntry(go);
            return go;
        }

        public void ExtWldInsertNpc(int npcInstanceIndex, string spawnPoint)
        {
            var userDataObject = AllocZkInstance(npcInstanceIndex);

            // InitInstance must run now, before STARTUP scripts call Npc_ChangeAttribute on this NPC.
            // If we defer to InitZkInstance (as before), Vm.InitInstance resets attributes to prototype
            // defaults AFTER STARTUP has already modified them (e.g. Nek's HP set to 0).
            // Setting IsZkInstanceInitialized=true tells InitZkInstance to skip re-running it.
            Vm.InitInstance(userDataObject.Instance);
            userDataObject.IsZkInstanceInitialized = true;

            // For mesh creation later, we need to store that there is a new NPC or a duplicate Monster to be spawned.
            _tmpWldInsertNpcData.Add((userDataObject, spawnPoint));
        }

        private NpcContainer AllocZkInstance(INpc vobNpc)
        {
            var symbol = _gameStateService.GothicVm.GetSymbolByName(vobNpc.NpcInstance);
            if (symbol == null)
            {
                Logger.LogWarning($"[NpcInitializerDomain] No Daedalus symbol for NPC '{vobNpc.NpcInstance}' (vobName='{vobNpc.Name}') — skipping.", LogCat.Npc);
                return null;
            }
            var userDataObject = AllocZkInstance(symbol.Index);
            // Run InitInstance against the fresh default NpcProxy BEFORE swapping in the save-game Vob.
            // If we swap first, Daedalus constructors (e.g. Npc_GetTalentValue) would access potentially
            // invalid talent objects from the ZenKit-deserialized save VOB and crash natively.
            Vm.InitInstance(userDataObject.Instance);
            userDataObject.IsZkInstanceInitialized = true;
            userDataObject.Vob = (NpcProxy)vobNpc;

            return userDataObject;
        }

        private NpcContainer AllocZkInstance(int npcIndex)
        {
            var npcSymbol = Vm.GetSymbolByIndex(npcIndex)!;
            var npcInstance = Vm.AllocInstance<NpcInstance>(npcSymbol);

            var userDataObject = new NpcContainer
            {
                Instance = npcInstance,
                Vob = new NpcProxy(npcIndex),
                Props = new(),
                InstanceId = _nextInstanceId++,
                SymbolIndex = npcIndex
            };
            
            // We reference our object as user data to retrieve it whenever a Daedalus External provides an NpcInstance as input.
            // With this, we can always switch between our Gothic data and ZenKit data.
            npcInstance.UserData = userDataObject;

            // IMPORTANT!: NpcInstance.UserData stores a weak pointer. i.e., if we do not store the local variable, it would get removed.
            _multiTypeCacheService.NpcCache.Add(userDataObject);

            return userDataObject;
        }

        /// <summary>
        /// Daedalus will walk through the whole Wld_InsertNpc() calls once.
        /// </summary>
        private void NewRunDaedalus()
        {
            // We need to set self=... --> Otherwise we get an NPE C_NPC.id/.name is not in NULL object
            Vm.GlobalSelf = Vm.GlobalHero;
            
            if(Vm.GetSymbolByName("STARTUP_GLOBAL") != null)
                _gameStateService.GothicVm.Call("STARTUP_GLOBAL");
            
            // Inside Startup.d, it's always STARTUP_{MAPNAME} and INIT_{MAPNAME}
            // FIXME - Inside Startup.d some Startup_*() functions also call Init_*() some not. How to handle properly? (Force calling it here? Even if done twice?)
            _gameStateService.GothicVm.Call($"STARTUP_{_saveGameService.CurrentWorldName.ToUpper().RemoveEnd(".ZEN")}");
            
            
            if(Vm.GetSymbolByName("INIT_GLOBAL") != null)
                Vm.Call("INIT_GLOBAL");

            _gameStateService.GothicVm.Call($"INIT_{_saveGameService.CurrentWorldName.ToUpper().RemoveEnd(".ZEN")}"); // call init as well, as per opengothic
        }

        /// <summary>
        /// Now we will create the NPCs step-by-step to ensure smooth loading screen fps.
        /// </summary>
        private async Task NewAddLazyLoading(LoadingService loading)
        {
            loading.SetPhase(nameof(WorldLoadingBarHandler.ProgressType.Npc), _tmpWldInsertNpcData.Count);

            foreach ((NpcContainer npc, string spawnPoint) element in _tmpWldInsertNpcData)
            {
                // Update the progress bar and check if we need to wait for the next frame now (As some conditions skip -continue- end of loop and would skip check)
                loading.Tick();
                await _frameSkipperService.TrySkipToNextFrame();

                element.npc.SpawnWaypoint = element.spawnPoint;
                var go = InitLazyLoadNpc(element.npc);

                var spawnPoint = GetSpawnPoint(element.npc, element.spawnPoint);
                if (spawnPoint == null)
                {
                    Logger.LogWarning($"Cannot spawn NPC as waypoint ${element.spawnPoint} does not exist.", LogCat.Npc);

                    // FIXME - Destroy GO and NPCInstance (Do not save the instance inside SaveGame as G1 is also removing it?)
                    continue;
                }

                if (spawnPoint.IsFreePoint())
                {
                    element.npc.Props.CurrentFreePoint = (FreePoint)spawnPoint;
                }
                else
                {
                    element.npc.Props.CurrentWayPoint = (WayPoint)spawnPoint;
                }

                go.transform.SetPositionAndRotation(spawnPoint.Position, spawnPoint.Rotation);
                _npcMeshCullingService.AddCullingEntry(go);
            }

            _tmpWldInsertNpcData.ClearAndReleaseMemory();
            
            // Full loading of NPCs is done.
            loading.FinalizePhase();
        }

        /// <summary>
        /// Initialize an NPC which is close to our hero in a save game.
        /// </summary>
        private void SaveGameAddLazyLoadingNearby(NpcContainer npc, INpc npcVob)
        {
            var go = InitLazyLoadNpc(npc);

            go.transform.SetPositionAndRotation(npcVob.Position.ToUnityVector(), npcVob.Rotation.ToUnityQuaternion());
            _npcMeshCullingService.AddCullingEntry(go);
        }

        /// <summary>
        /// Basically the same logic as SaveGameAddLazyLoadingNearby() but we use the Routine's WP to get the position from.
        /// </summary>
        private void SaveGameAddLazyLoadingAnywhere(NpcContainer npc, string fallbackWayPoint)
        {
            var go = InitLazyLoadNpc(npc);

            var spawnPoint = GetSpawnPoint(npc, fallbackWayPoint);

            // When loading a save game from G1, e.g. TPL_1401_GorNaKosh's WP is wrong. His WP only exists in Old Mine,
            // but he is also named inside STARTUP_SUB_PSICAMP() which is on world.zen. Simply removing them for now.
            if (spawnPoint == null)
            {
                Logger.LogWarning($"Cannot spawn NPC {npc.Instance.GetName(NpcNameSlot.Slot0)} as waypoint " +
                                  $"{npc.Props.RoutineCurrent?.Waypoint}/{fallbackWayPoint} does not exist.", LogCat.Npc);
                Object.Destroy(go);
                return;
            }
            if (spawnPoint.IsFreePoint())
                npc.Props.CurrentFreePoint = (FreePoint)spawnPoint;
            else
                npc.Props.CurrentWayPoint = (WayPoint)spawnPoint;
            go.transform.SetPositionAndRotation(spawnPoint.Position, spawnPoint.Rotation);

            // Prepare some variables, which need to be calculated from save game.
            // We simply say: Restart whole logic for NPCs  .
            // FIXME - It's a hack for now, as the normal Vob.*Routine/*State variables aren't handled as of now.
            npc.Props.CurrentLoopState = NpcProperties.LoopState.None;
            npc.Vob.CurrentStateValid = false;
            npc.Vob.NextStateValid = false;
            
            _npcMeshCullingService.AddCullingEntry(go);
        }

        // Fallback counter for monsters with no spawn waypoint (edge case).
        private int _monsterIndex;
        // Per-waypoint counter for monsters — handles multiple monsters on the same WP.
        private readonly Dictionary<string, int> _monsterWaypointCount = new();

        /// <summary>
        /// InitZkInstance and create a GameObject for the NPC to be loaded later.
        /// </summary>
        public GameObject InitLazyLoadNpc(NpcContainer npc)
        {
            InitZkInstance(npc);
            var go = new GameObject();
            go.SetParent(RootGo);

            if (npc.Instance.Id > 0)
            {
                go.name = $"{npc.Instance.GetName(NpcNameSlot.Slot0)} ({npc.Instance.Id})";
            }
            else if (!string.IsNullOrEmpty(npc.SpawnWaypoint))
            {
                // Use waypoint as stable monster key — immune to ordering/counter drift between sessions.
                // Multiple monsters on the same WP get a suffix: (@WP_X), (@WP_X_1), (@WP_X_2), ...
                _monsterWaypointCount.TryGetValue(npc.SpawnWaypoint, out var idx);
                _monsterWaypointCount[npc.SpawnWaypoint] = idx + 1;
                var suffix = idx == 0 ? $"@{npc.SpawnWaypoint}" : $"@{npc.SpawnWaypoint}_{idx}";
                go.name = $"{npc.Instance.GetName(NpcNameSlot.Slot0)} ({suffix})";
            }
            else
            {
                go.name = $"{npc.Instance.GetName(NpcNameSlot.Slot0)} ({_monsterIndex++})";
            }

            npc.GoName = go.name;

            var loader = go.AddComponent<NpcLoader>();
            loader.Npc = npc.Instance;

            return go;
        }

        private void InitZkInstance(NpcContainer npc)
        {
            // Skip Vm.InitInstance() if already called in ExtWldInsertNpc (new game path).
            // For save game NPCs (not pre-initialized), we still need to run it so externals like
            // Npc_SetTalentValue() work — but then we overwrite the prototype defaults with saved values.
            if (!npc.IsZkInstanceInitialized)
                Vm.InitInstance(npc.Instance);

            npc.Vob.CopyFromInstanceData(npc.Instance);

            // Save game: Instance was just reset to prototype defaults by Vm.InitInstance().
            // Restore the actual saved runtime values (HP, AiVars, level…) from the Vob back into the Instance.
            if (_configService.Dev.EnableSaveLoadSystem && !npc.Vob.IsNew)
                npc.Vob.RestoreInstanceFromVob(npc.Instance);

            // NpcInstance is the initialized Daedalus Instance which contains initial data.
            // Vob.Npc contains runtime information. If no runtime information is set (new game started / world entered for the first time), we use the initial data.
            if (npc.Vob.CurrentRoutine.IsNullOrEmpty())
            {
                npc.Vob.CurrentRoutine = _gameStateService.GothicVm.GetSymbolByIndex(npc.Instance.DailyRoutine)!.Name;
            }

            _npcRoutineService.ExchangeRoutine(npc.Instance, npc.Vob.CurrentRoutine);
        }

        public void InitNpc(NpcInstance npcInstance, GameObject lazyLoadGo)
        {
            var npcData = npcInstance.GetUserData();
            var newNpc = _resourceCacheService.TryGetPrefabObject(PrefabType.Npc, parent: lazyLoadGo)!;
            var props = npcData.Props;

            // We set the root of Prefab as the new Root object. LazyLoading Root-GO isn't needed for anything, but it's name anymore.
            newNpc.name = "Root";
            npcData.Go = newNpc;

            lazyLoadGo.transform.GetPositionAndRotation(out var lazyPos, out var lazyRot);

            var finalSpawnPos = GetFreeAreaAtSpawnPoint(lazyPos);

            var mdhName = string.IsNullOrEmpty(props.MdhNameOverlay)
                ? props.MdhNameBase
                : props.MdhNameOverlay;
            _meshService.CreateNpc(newNpc.name, props.MdmName, mdhName, props.BodyData,
                finalSpawnPos, lazyRot, lazyLoadGo, newNpc);

            // We don't need specific locations of initial LazyLoading GO anymore.
            lazyLoadGo.transform.SetPositionAndRotation(default, default);

            foreach (var equippedItem in props.EquippedItems)
            {
                _meshService.CreateNpcWeapon(newNpc, equippedItem, (VmGothicEnums.ItemFlags)equippedItem.MainFlag,
                    (VmGothicEnums.ItemFlags)equippedItem.Flags);
            }
            
            // Some monsters have equipped weapons directly in their hands.
            if (props.CurrentItem > 0)
            {
                var weaponInHand = _vmCacheService.TryGetItemData(props.CurrentItem);
                _meshService.CreateNpcWeapon(newNpc, weaponInHand, (VmGothicEnums.ItemFlags)weaponInHand.MainFlag,
                    (VmGothicEnums.ItemFlags)weaponInHand.Flags, true);
            }
        }

        [CanBeNull]
        private WayNetPoint GetSpawnPoint(NpcContainer npc, string fallbackSpawnPoint)
        {
            // Find the right spawn point based on the currently active routine.
            if (npc.Props.RoutineCurrent != null)
            {
                var routineSpawnPointName = npc.Props.RoutineCurrent.Waypoint;
                var wp = _wayNetService.GetWayNetPoint(routineSpawnPointName);

                // Some Routines have a misspelled WP name. (e.g. Graham at 8am [..]OUSIDE[...] - >T< missing)
                // We will therefore do a fallback to the previous routine.
                if (wp == null)
                    return _wayNetService.GetWayNetPoint(npc.Props.RoutinePrevious.Waypoint);
                else
                    return wp;
            }
            // Fallback: If no routine exists, spawn at the spot which is named inside Wld_insertNpc()
            else
            {
                return _wayNetService.GetWayNetPoint(fallbackSpawnPoint);
            }
        }

        /// <summary>
        /// Check if NPC/Monster will spawn inside another and do a circulated free V3 check around the area.
        /// </summary>
        public Vector3 GetFreeAreaAtSpawnPoint(Vector3 positionToScan)
        {
            var isPositionFound = false;
            var testRadius = 1f; // ~2x size of normal bounding box of an NPC.
            // Some FP/WP are on a hill. The spawn check will therefore lift the location for a little to not interfere with world mesh collision check.
            var groundControlDifference = new Vector3(0, 1f, 0);
            var initialSpawnPointGroundControl = positionToScan + groundControlDifference;

            // Check if the spawn point is free.
            if (!Physics.CheckSphere(initialSpawnPointGroundControl, testRadius / 2))
            {
                return positionToScan;
            }

            // Alternatively let's circle around the spawn point if multiple NPCs spawn onto the same one.
            // Circle around at least x-times.
            // G1: Orc-dogs are in a big crowd. We therefore need to draw a location circle multiple times to spawn them all.
            for (var currentRadius = testRadius; currentRadius <= testRadius * 2; currentRadius += testRadius)
            {
                for (var angle = 0f; angle < 360f; angle += 36f)
                {
                    var angleInRadians = angle * Mathf.Deg2Rad;
                    var offsetPoint = new Vector3(Mathf.Cos(angleInRadians) * currentRadius, 0,
                        Mathf.Sin(angleInRadians) * currentRadius);
                    var checkPointGroundControl = initialSpawnPointGroundControl + offsetPoint;

                    // Check if the point is clear (no obstacles)
                    if (!Physics.CheckSphere(checkPointGroundControl, testRadius / 2))
                    {
                        return positionToScan + offsetPoint;
                    }
                }
            }

            Logger.LogError(
                $"No suitable spawn point found for NPC at >{positionToScan}<. Circle search didn't find anything!", LogCat.Npc);
            return default;
        }
    }
}
