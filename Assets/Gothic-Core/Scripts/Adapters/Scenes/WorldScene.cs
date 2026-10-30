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
        [Inject] private readonly FrameSkipperService _frameSkipperService;

        // VOBs and NPCs this close to the spawn point are built before the view is shown (m). Lights reach the ground
        // up to StationaryLightsService's 80 m, but the near ones matter most and keep the black short.
        private const float _surroundingsRadius = 50f;

        // Longest wait for them (s). Whatever is still missing then appears while the player looks around.
        private const float _surroundingsMaxWait = 5f;

        // The camera counts as arrived at the spawn point this close (m, horizontal; VR play area offset included).
        private const float _cameraArrivalDistance = 5f;
        private const float _cameraArrivalMaxWait = 1f; // s

        // Frames culling needs after the camera arrived to queue the objects around it.
        private const int _cullingUpdateFrames = 2;

        private Vector3? _spawnPosition;


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

                // Black until the surroundings are built (see BuildSurroundingsAsync), so they don't pop in.
                _contextInteractionService.SetViewHidden(true);
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

                // Culling only _enqueues_ the VOBs and NPCs around the spawn point for lazy initialization. The near
                // ones are built now, the rest in the background, nearest first.
                await BuildSurroundingsAsync();
                watch.LogAndRestart("Surroundings built");

                _loadingService.StopLoading();
                SceneManager.UnloadSceneAsync(Constants.SceneLoading);
            }
            catch(Exception ex)
            {
                Logger.LogError(ex.ToString(), LogCat.Loading);
            }
            finally
            {
                _contextInteractionService.SetViewHidden(false);
                _contextInteractionService.EnableMenus();
                fullWatch.Log("Full world loaded");
            }
        }

        /// <summary>
        /// Waits until the queued VOBs and NPCs around the spawn point are built. The screen is black meanwhile, so
        /// the queues may use long frames (FrameSkipperService.IsViewHidden). With half a frame per frame (the
        /// default), a crowded place like the Old Camp popped in over several seconds.
        /// </summary>
        private async Task BuildSurroundingsAsync()
        {
            if (_spawnPosition == null)
                return;

            var center = _spawnPosition.Value;
            var watch = Stopwatch.StartNew();
            _frameSkipperService.IsViewHidden = true;
            _vobService.SetInitFocus(center, _surroundingsRadius);
            _npcService.SetInitFocus(center, _surroundingsRadius);
            try
            {
                // The VR rig applies a teleport in its next update, so the camera is still at the loading screen
                // here. Culling queues the VOBs and NPCs around the spawn point once the camera is there.
                while (!IsCameraNear(center) && watch.Elapsed.TotalSeconds < _cameraArrivalMaxWait)
                    await Task.Yield();
                for (var i = 0; i < _cullingUpdateFrames; i++)
                    await Task.Yield();

                while (_vobService.HasQueuedVobWithin(center, _surroundingsRadius) ||
                       _npcService.HasQueuedNpcWithin(center, _surroundingsRadius))
                {
                    if (watch.Elapsed.TotalSeconds >= _surroundingsMaxWait)
                    {
                        Logger.LogWarning($"Surroundings not built after {_surroundingsMaxWait} s. " +
                                          "The rest appears in the background.", LogCat.Loading);
                        break;
                    }

                    await Task.Yield();
                }
            }
            finally
            {
                _frameSkipperService.IsViewHidden = false;
                _vobService.ClearInitFocus();
                _npcService.ClearInitFocus();
            }

            Logger.Log($"Surroundings within {_surroundingsRadius} m built after {watch.Elapsed.TotalSeconds:0.00} s.",
                LogCat.Loading);
        }

        private static bool IsCameraNear(Vector3 position)
        {
            var camera = Camera.main;
            if (camera == null)
                return false;

            var offset = camera.transform.position - position;
            offset.y = 0;
            return offset.sqrMagnitude < _cameraArrivalDistance * _cameraArrivalDistance;
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
                        TeleportPlayerToStart(debugGo.transform.position, debugGo.transform.rotation);
                        return;
                    }
                }
            }

            // 2.
            if (_playerService.HeroSpawnPosition != default)
            {
                TeleportPlayerToStart(_playerService.HeroSpawnPosition, _playerService.HeroSpawnRotation);
                return;
            }
            
            var spots = _gameStateService.FreePoints;
            KeyValuePair<string, FreePoint> startPoint;
            
            // 3.
            if(_playerService.LastLevelChangeTriggerVobName != null)
            {
                startPoint = spots.FirstOrDefault(
                    go => go.Key.EqualsIgnoreCase(_playerService.LastLevelChangeTriggerVobName));
                TeleportPlayerToStart(startPoint.Value.Position, startPoint.Value.Rotation);
                _playerService.LastLevelChangeTriggerVobName = null;
                return;
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

            TeleportPlayerToStart(startPoint.Value.Position, startPoint.Value.Rotation);
        }

        private void TeleportPlayerToStart(Vector3 position, Quaternion rotation)
        {
            _spawnPosition = position;
            _contextInteractionService.TeleportPlayerTo(position, rotation);
            _contextInteractionService.UnlockPlayer();
            _playerService.ResetSpawn();
        }
    }
}
