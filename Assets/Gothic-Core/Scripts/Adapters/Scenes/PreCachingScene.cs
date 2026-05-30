using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using GUZ.Core.Adapters.UI.LoadingBars;
using GUZ.Core.Const;
using GUZ.Core.Domain.StaticCache;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Manager;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Context;
using GUZ.Core.Services.StaticCache;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Adapters.Scenes
{
    public class PreCachingScene : MonoBehaviour, IScene
    {
        [SerializeField] private PreCachingLoadingBarHandler _loadingBarHandler;

        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly LoadingService _loadingService;
        [Inject] private readonly StaticCacheService _staticCacheService;
        [Inject] private readonly BootstrapService _bootstrapService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly AudioService _audioService;
        
        private const string _worldsPath = "/_work/Data/Worlds";

        
        public void Init()
        {
            _contextInteractionService.DisableMenus();
            _contextInteractionService.InitUIInteraction();
            _contextInteractionService.TeleportPlayerTo(_loadingBarHandler.transform.position);

#pragma warning disable CS4014 // Do not wait. We want to update player movement (VR) and camera view (progress bar)
            CreateCaches();
#pragma warning restore CS4014
        }

        /// <summary>
        /// 1. Fetch all existing worlds
        ///
        /// 1.1 Load static VOBs (many elements except like Spot+Startpoint+Iems+[Elements with have no mesh])
        ///   Cache them
        ///
        /// 1.2 Load world mesh of a level
        ///   Sliced by lighting VOBs
        ///   Cache it
        /// </summary>
        private async Task CreateCaches()
        {
            try
            {
                var worldsRootFolder = _resourceCacheService.Vfs.Resolve(_worldsPath);
                var allWorldNames = new List<string>();
                GetWorldsFromVfs(worldsRootFolder, allWorldNames);

                var worldNamesToLoad = new List<string>();
                var worldsToLoad = new List<IWorld>();

                foreach (var worldNameToCheck in allWorldNames)
                {
                    var world = _resourceCacheService.TryGetWorld(worldNameToCheck, _contextGameVersionService.Version);

                    // e.g., FIRETREE.ZEN is no real world.
                    if (world == null || world.Mesh.PositionCount == 0)
                        continue;
                    
                    worldNamesToLoad.Add(worldNameToCheck);
                    worldsToLoad.Add(world);
                }

                // DEBUG - only cache one world for faster tests
                if (_configService.Dev.OnlyCreateCacheForWorld.NotNullOrEmpty())
                {
                    worldsToLoad.Clear();
                    worldNamesToLoad.Clear();
                    worldNamesToLoad.Add(_configService.Dev.OnlyCreateCacheForWorld);
                    worldsToLoad.Add(_resourceCacheService.TryGetWorld(_configService.Dev.OnlyCreateCacheForWorld,
                        _contextGameVersionService.Version));
                }
                
                if (!_configService.Dev.AlwaysRecreateCache && _staticCacheService.DoCacheFilesExist(worldNamesToLoad))
                {
                    var metadata = await _staticCacheService.ReadMetadata();
                    if (metadata.Version == Constants.StaticCacheVersion)
                    {
                        Logger.Log("World + Global data is already cached and metadata version matches. Skipping...", LogCat.PreCaching);
                        _bootstrapService.LoadScene(Constants.SceneLogo, Constants.ScenePreCaching);
                        return;
                    }
                }
                else
                {
                    Logger.Log($"World + Global data is not (fully) cached or metadata version doesn't match. Recreating now for: [{string.Join(", ", worldNamesToLoad)}]", LogCat.PreCaching);
                }
                
                //
                // Now we (re)create whole cache.
                //
                
                // Sleeper temple music (similar to installation music)
                // FIXME - Find music for G2 to play in here
                if (_contextGameVersionService.Version == GameVersion.Gothic1)
                    _audioService.PlayMusic("KAT_DAY_STD");
                
                _contextInteractionService.DisableMenus();
                _loadingBarHandler.LevelCount = worldsToLoad.Count;
                _loadingService.InitLoading(_loadingBarHandler);

                var watch = Stopwatch.StartNew();
                var overallWatch = Stopwatch.StartNew();

                var vobBoundsCache = new VobBoundsCacheCreatorDomain().Inject(); // FIXME - We need to move it to a Domain object and call it via Service.
                var vobColliderCache = new VobItemColliderCacheCreatorDomain().Inject();
                var textureArrayCache = new TextureArrayCacheCreatorDomain().Inject();

                _staticCacheService.InitCacheFolder();

                for (var worldIndex = 0; worldIndex < worldsToLoad.Count; worldIndex++)
                {
                    var world = worldsToLoad[worldIndex];
                    var worldName = worldNamesToLoad[worldIndex];
                        
                    Logger.Log($"### PreCaching meshes for world: {worldName}", LogCat.PreCaching);
                    var stationaryLightCache = new StationaryLightCacheCreatorDomain().Inject();
                    var worldChunkCache = new WorldChunkCacheCreatorDomain().Inject();

                    await vobBoundsCache.CalculateVobBounds(world.RootObjects, worldIndex);
                    watch.LogAndRestart($"{worldName}: VobBounds calculated.");

                    await textureArrayCache.CalculateTextureArrayInformation(world.Mesh, worldIndex);
                    watch.LogAndRestart($"{worldName}: WorldMesh TextureArray calculated.");

                    await textureArrayCache.CalculateTextureArrayInformation(world.RootObjects, worldIndex);
                    watch.LogAndRestart($"{worldName}: World VOBs TextureArray calculated.");

                    await stationaryLightCache.CalculateStationaryLights(world.RootObjects, worldIndex);
                    watch.LogAndRestart($"{worldName}: Stationary lights calculated.");

                    await worldChunkCache.CalculateWorldChunks(world, stationaryLightCache.StationaryLightBounds, worldIndex);
                    watch.LogAndRestart($"{worldName}: World chunks calculated.");

                    await _staticCacheService.SaveWorldCache(worldName, worldChunkCache.MergedChunksByLights, stationaryLightCache.StationaryLightInfos);

                    // DEBUG - Re-enable only when needed.
                    // await _staticCacheService.SaveDebugCache(worldName, stationaryLightCache.StationaryLightBounds);

                    // DEBUG restore
                    // {
                    //     var loadRoot = new GameObject("DebugRestore");
                    //     loadRoot.transform.position = new(1000, 0, 0);
                    //
                    //     await _staticCacheService.LoadCache(loadRoot, worldName);
                    //
                    //     Logger.Log("DEBUG Loading done!");
                    //     return;
                    // }
                }

                await textureArrayCache.CalculateItemTextureArrayInformation();
                watch.LogAndRestart("Global: Texture array for oCItems calculated.");

                await vobBoundsCache.CalculateVobItemBounds();
                watch.LogAndRestart("VobBounds for oCItems calculated.");

                await vobColliderCache.CalculateVobItemColliderCache(vobBoundsCache.Bounds);
                watch.LogAndRestart("Collider for oCItems calculated.");

                await _staticCacheService.SaveGlobalCache(vobBoundsCache.Bounds, vobColliderCache.ItemCollider, textureArrayCache.TextureArrayInformation);
                watch.LogAndRestart("Saved GlobalCache files.");
                overallWatch.Log("Overall PreCaching done.");

                // Cleanup
                _multiTypeCacheService.Dispose();

                // Every world of the game is cached successfully. Now let's move on!
                _bootstrapService.LoadScene(Constants.SceneLogo, Constants.ScenePreCaching);
            }
            catch (Exception e)
            {
                Logger.LogError(e.ToString(), LogCat.PreCaching);
                throw;
            }
            finally
            {
                _loadingService.StopLoading();

                // We need to grant the player always the option to quit the game via menu if something fails.
                _contextInteractionService.EnableMenus();
            }
        }
        
        private void GetWorldsFromVfs(VfsNode folder, List<string> worlds)
        {
            foreach (var child in folder.Children)
            {
                if (child.IsDir())
                    GetWorldsFromVfs(child, worlds);
                else if (child.Name.EndsWithIgnoreCase(".zen"))
                    worlds.Add(child.Name);
            }
        }
    }
}
