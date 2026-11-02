using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Gothic.Core.Const;
using Gothic.Core.Creator;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vob.WayNet;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.StaticCache;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Scenes
{
    public class WorldScene : MonoBehaviour, IScene
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly SkyService _skyService;
        [Inject] private readonly LoadingService _loadingService;
        [Inject] private readonly StaticCacheService _staticCacheService;
        [Inject] private readonly StationaryLightsService _stationaryLightsService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        
        
        public void Init()
        {
#pragma warning disable CS4014 // Do not wait. We want to update player movement (VR) and camera view (progress bar)
            LoadWorldContentAsync();
#pragma warning restore CS4014
        }
        
        /// <summary>
        /// Order of loading:
        /// 1. Cache data
        /// 2. World - Mesh of the world
        /// 3. WayPoints - Needed for spawning NPCs when world is loaded the first time
        /// 4. VOBs - First entry, as we slice world chunks based on light VOBs
        /// 5. NPCs - If we load the world for the first time, we leverage their current routine's values
        /// 6. Stationary lights
        /// </summary>
        private async Task LoadWorldContentAsync()
        {
            _contextInteractionService.DisableMenus();
            
            var watch = Stopwatch.StartNew();

            var worldRoot = new GameObject("World");
            var vobRoot = new GameObject("VOBs");
            var npcRoot = new GameObject("NPCs");
            // We need to disable all vob meshes during loading. Otherwise, loading time will increase from 10 seconds to 10 minutes. ;-)
            worldRoot.SetActive(false);
            vobRoot.SetActive(false);
            npcRoot.SetActive(false);

            var fullWatch = Stopwatch.StartNew();
            try
            {
                // 1.1
                // Global cache and global calculations (TextureArray) only need to be done once.
                if (!_staticCacheService.IsGlobalCacheLoaded)
                {
                    // Load global Static cache and arrange it in memory
                    await _staticCacheService.LoadGlobalCache();
                    watch.LogAndRestart("StaticCache - Global loaded");

                    await _meshService.CreateTextureArray();
                    watch.LogAndRestart("Texture array created");
                }

                // 1.2
                // Load world cache
                await _staticCacheService.LoadWorldCache(_saveGameService.CurrentWorldName).AwaitAndLog();
                watch.LogAndRestart("StaticCache - World loaded");

                // 2. Load world based on cached Chunks
                if (_configService.Dev.EnableWorldMesh)
                {
                    await _meshService.CreateWorld(
                        _staticCacheService.LoadedWorldChunks,
                        _saveGameService.CurrentWorldData.Mesh,
                        _loadingService,
                        worldRoot
                    ).AwaitAndLog();
                    watch.LogAndRestart("World loaded");
                }

                // 3. WayNet
                _wayNetService.Create(_configService.Dev, _saveGameService.CurrentWorldData);
                watch.LogAndRestart("WayNet initialized");


                // 4. VOBs
                // Build the world and vob meshes, populating the texture arrays.
                // We need to start creating Vobs as we need to calculate world slicing based on amount of lights at a certain space afterward.
                if (_configService.Dev.EnableVOBs)
                {
                    // If we load a SaveGame, then nearby NPCs are stored as VOB and will be created as GOs inside NpcManager. We need to prepare it before.
                    _npcService.SetRootGo(npcRoot);

                    await _vobService.CreateWorldVobsAsync(_configService.Dev, _loadingService, _saveGameService.CurrentWorldData.Vobs, vobRoot)
                        .AwaitAndLog();
                    watch.LogAndRestart("VOBs created");
                }

                // 5. NPCs
                // If the world is visited for the first time, then we need to load Npcs via Wld_InsertNpc()
                _npcService.CacheHero();
                if (_configService.Dev.EnableNpcs)
                {
                    // await NpcCreator.CreateAsync(config.Dev, _loadingService).AwaitAndLog();
                    await _npcService.CreateWorldNpcs(_loadingService).AwaitAndLog();
                    watch.LogAndRestart("NPCs created");
                }

                // 6. Stationary lights
                // They are affecting (1) World Mesh and (2) VOB meshes.
                // We therefore need to initialize them after both are created.
                _stationaryLightsService.InitStationaryLights();
                watch.LogAndRestart("Stationary lights initialized");


                // World fully loaded
                // TODO - Does this call add benefits for memory?
                _resourceCacheService.ReleaseLoadedData();

                // TODO - Still needed?
                worldRoot.SetActive(true);
                vobRoot.SetActive(true);
                npcRoot.SetActive(true);

                _skyService.InitWorld();

                TeleportPlayerToStart();

                // There are many handlers which listen to this event. If any of these fails, we won't get notified without a try-catch.
                try
                {
                    GlobalEventDispatcher.WorldSceneLoaded.Invoke();
                }
                catch (Exception e)
                {
                    Logger.LogError(e.ToString(), LogCat.Loading);
                }

                // The culling sweep inside WorldSceneLoaded only _enqueued_ all VOBs around the spawn point for
                // lazy initialization. InitVobCoroutine() drains the queue in the background while the player is
                // already in the world - a brief pop-in is preferred over holding the loading screen any longer.
                _loadingService.StopLoading();
                SceneManager.UnloadSceneAsync(Constants.SceneLoading);
            }
            catch(Exception ex)
            {
                Logger.LogError(ex.ToString(), LogCat.Loading);
            }
            finally
            {
                _contextInteractionService.EnableMenus();
                fullWatch.Log("Full world loaded");
            }
        }

        /// <summary>
        /// There are three options, where the player can spawn:
        /// 1. We set it inside GameConfiguration.SpawnAtWaypoint (only during first load of the game)
        /// 2. We got the spawn information from a loaded SaveGame from the Hero's VOB
        /// 3. We get the waypoint to spawn at from a ChangeLevel trigger
        /// 4. We load the START_* waypoint
        /// </summary>
        private void TeleportPlayerToStart()
        {
            // 1.
            var debugSpawnAtWayPoint = _configService.Dev.SpawnAtWaypoint;

            // If we currently load world from a save game, we will use the stored hero position which was set during VOB loading.
            if (_saveGameService.IsFirstWorldLoadingFromSaveGame)
            {
                // We only use the Vob location once per save game loading.
                _saveGameService.IsFirstWorldLoadingFromSaveGame = false;

                if (debugSpawnAtWayPoint.NotNullOrEmpty())
                {
                    // We need to look up WPs and FPs. Therefore this slow check (which is fine, as it's only done for debugging purposes)
                    var debugGo = GameObject.Find(debugSpawnAtWayPoint);

                    if (debugGo != null)
                    {
                        Logger.Log($"[Spawn] Debug waypoint '{debugSpawnAtWayPoint}'", LogCat.Loading);
                        TeleportPlayerToStart(debugGo.transform.position, debugGo.transform.rotation);
                        return;
                    }
                }
            }

            // 2.
            if (_playerService.HeroSpawnPosition != default)
            {
                Logger.Log($"[Spawn] Stored hero position {_playerService.HeroSpawnPosition}", LogCat.Loading);
                TeleportPlayerToStart(_playerService.HeroSpawnPosition, _playerService.HeroSpawnRotation);
                return;
            }
            
            var spots = _gameStateService.FreePoints;
            KeyValuePair<string, FreePoint> startPoint;
            
            // 3.
            if(_playerService.LastLevelChangeTriggerVobName != null)
            {
                var startVobName = _playerService.LastLevelChangeTriggerVobName;
                _playerService.LastLevelChangeTriggerVobName = null;
                startPoint = spots.FirstOrDefault(go => go.Key.EqualsIgnoreCase(startVobName));
                if (startPoint.Value != null)
                {
                    Logger.Log($"[Spawn] Level change start '{startVobName}'", LogCat.Loading);
                    TeleportPlayerToStart(startPoint.Value.Position, startPoint.Value.Rotation);
                    return;
                }

                // DeveloperConfig.EnableLevelChangeSpawnFallback: the trigger's start VOB can be a waypoint or any
                // named VOB, or missing (Mroczne Tajemnice's ANCIENTTEMPLE sends the hero back to TEMPLE_TO_CAVE,
                // which ABANDONEDMINE doesn't have) - the start point lookup crashed and the loading screen stalled.
                if (_configService.Dev.EnableLevelChangeSpawnFallback &&
                    TryFindLevelChangeSpawn(startVobName, out var position, out var rotation))
                {
                    TeleportPlayerToStart(position, rotation);
                    return;
                }

                Logger.LogWarning($"Level change start VOB '{startVobName}' not found - spawning at START like the " +
                                  "engine", LogCat.Loading);
            }

            // 4.
            startPoint = spots.FirstOrDefault(
                go => go.Key.EqualsIgnoreCase("START") || go.Key.EqualsIgnoreCase("START_GOTHIC2")
            );

            if (startPoint.Key.IsNullOrEmpty())
            {
                Logger.LogError("No suitable START_* waypoint found!", LogCat.Loading);
                return;
            }

            Logger.Log($"[Spawn] World start '{startPoint.Key}' {startPoint.Value.Position}", LogCat.Loading);
            TeleportPlayerToStart(startPoint.Value.Position, startPoint.Value.Rotation);
        }

        /// <summary>
        /// The start VOB as a waypoint or any named VOB; when it's missing, the way point next to the trigger leading
        /// back to the world we came from (outside its box, or it fires again). The engine would use START, which
        /// can be over the void (Mroczne Tajemnice's abandoned mine).
        /// </summary>
        private bool TryFindLevelChangeSpawn(string startVobName, out Vector3 position, out Quaternion rotation)
        {
            var wayPoint = _gameStateService.WayPoints.Values.FirstOrDefault(i => i.Name.EqualsIgnoreCase(startVobName));
            if (wayPoint != null)
            {
                Logger.Log($"Level change start '{startVobName}' is a waypoint", LogCat.Loading);
                position = wayPoint.Position;
                rotation = wayPoint.Rotation;
                return true;
            }

            var vobs = _saveGameService.CurrentWorldData.Vobs;
            var startVob = FindVob(vobs, i => i.Name.EqualsIgnoreCase(startVobName));
            if (startVob != null)
            {
                Logger.Log($"Level change start '{startVobName}' is a {startVob.Type}", LogCat.Loading);
                position = startVob.Position.ToUnityVector();
                rotation = startVob.Rotation.ToUnityQuaternion();
                return true;
            }

            var fromWorld = _playerService.LastLevelChangeFromWorld;
            var backTrigger = fromWorld == null
                ? null
                : FindVob(vobs, i => i is ITriggerChangeLevel trigger && trigger.LevelName.EqualsIgnoreCase(fromWorld));
            if (backTrigger != null)
            {
                var bounds = backTrigger.BoundingBox.ToUnityBounds();
                bounds.Expand(2f);
                // Height counts triple: a point on another floor (a cave level below) is no way back.
                var nearest = _gameStateService.WayPoints.Values.Cast<WayNetPoint>()
                    .Concat(_gameStateService.FreePoints.Values)
                    .Where(i => !bounds.Contains(i.Position))
                    .OrderBy(i =>
                    {
                        var offset = i.Position - bounds.center;
                        offset.y *= 3f;
                        return offset.sqrMagnitude;
                    })
                    .FirstOrDefault();
                if (nearest != null)
                {
                    Logger.LogWarning($"Level change start VOB '{startVobName}' not found - spawning at " +
                                      $"'{nearest.Name}' next to the trigger '{backTrigger.Name}' back to " +
                                      $"'{fromWorld}'", LogCat.Loading);
                    position = nearest.Position;
                    var toTrigger = Vector3.ProjectOnPlane(bounds.center - nearest.Position, Vector3.up);
                    rotation = toTrigger.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toTrigger) : Quaternion.identity;
                    return true;
                }
            }

            position = default;
            rotation = default;
            return false;
        }

        private static IVirtualObject FindVob(List<IVirtualObject> vobs, Func<IVirtualObject, bool> match)
        {
            foreach (var vob in vobs)
            {
                if (match(vob))
                    return vob;
                var child = FindVob(vob.Children, match);
                if (child != null)
                    return child;
            }
            return null;
        }

        private void TeleportPlayerToStart(Vector3 position, Quaternion rotation)
        {
            _contextInteractionService.TeleportPlayerTo(position, rotation);
            _contextInteractionService.UnlockPlayer();
            _playerService.ResetSpawn();
        }
    }
}
