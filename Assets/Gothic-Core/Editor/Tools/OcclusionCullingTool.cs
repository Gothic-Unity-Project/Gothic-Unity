using System;
using System.Threading.Tasks;
using GUZ.Core.Domain.StaticCache;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Meshes;
using GUZ.Core.Services.StaticCache;
using GUZ.G1;
using GUZ.G2.Services.Context;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZenKit;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Editor.Tools
{
    /// How to use:
    /// 1. Load the scene for which you want the Occlusion Culling being baked
    /// 2. Run "UnZENity/Occlusion Culling/Load world mesh for G1/G2" in editor
    /// 3. Open "Window/Rendering/Occlusion Culling"
    /// 4. Bake OC data
    /// 5. Save the scene
    /// 6. Put the data you have into the binary repository at https://github.com/Gothic-UnZENity-Project/binary-dependencies/tree/main/UnZENity-Core/Scenes/Worlds
    /// 7. Update your Gothic-UnZENity version (Package Manager --> Update binary-dependencies package)
    /// 8. Test if OC data is used in a normal game
    public class OcclusionCullingTool : EditorWindow
    {
        [MenuItem("UnZENity/Build/Occlusion Culling/Load world mesh for G1", true)]
        private static bool ValidateG1OCLoading()
        {
            // If game is in playmode, disable button.
            return !EditorApplication.isPlaying;
        }

        [MenuItem("UnZENity/Build/Occlusion Culling/Load world mesh for G2", true)]
        private static bool ValidateG2OCLoading()
        {
            // If game is in playmode, disable button.
            return !EditorApplication.isPlaying;
        }

        [MenuItem("UnZENity/Build/Occlusion Culling/Load world mesh for G1", priority = 1000)]
        public static async Task LoadWorldMeshG1()
        {
            await LoadWorldMesh(GameVersion.Gothic1);
        }

        [MenuItem("UnZENity/Build/Occlusion Culling/Load world mesh for G2", priority = 1001)]
        public static async Task LoadWorldMeshG2()
        {
            await LoadWorldMesh(GameVersion.Gothic2);
        }

        private static async Task LoadWorldMesh(GameVersion version)
        {
            // Do not show Window when game is started.
            if (Application.isPlaying)
            {
                return;
            }

            // Prepare configuration needed during execution.
            var config = new ConfigService();
            config.LoadRootJson(); 
            
            // FIXME - Inject won't work at EditorTime. Find another way.
            // ResourceCacheService.Init(version == GameVersion.Gothic1 ? config.Root.Gothic1Path : config.Root.Gothic2Path);

            await Execute(version).AwaitAndLog();
        }

        private static async Task Execute(GameVersion version)
        {
            // // Needed during Cache creation. (StationaryLightCache is calling worlds from Fire.zen and need to have this information.)
            // GameContext.ContextGameVersionService = version == GameVersion.Gothic1 ? new G1ContextService() : new G2ContextService();
            //
            // var worldName = SceneManager.GetActiveScene().name;
            //
            // // FIXME - Inject won't work at EditorTime. Find another way.
            // IWorld world = null; // _resourceCacheService.TryGetWorld(worldName, version)!;
            // Logger.LogEditor("DONE - Loading world from ZenKit", LogCat.PreCaching);
            //
            // if (world == null)
            // {
            //     throw new ArgumentException($"Current scene >{worldName}< is no gothic world.");
            // }
            //
            // var stationaryLightCache = new StationaryLightCacheCreatorDomain().Inject();
            // var worldChunkCache = new WorldChunkCacheCreatorDomain().Inject();
            //
            // await stationaryLightCache.CalculateStationaryLights(world.RootObjects, 0).AwaitAndLog();
            // Logger.LogEditor("DONE - Loading stationary light data", LogCat.PreCaching);
            // await worldChunkCache.CalculateWorldChunks(world, stationaryLightCache.StationaryLightBounds, 0).AwaitAndLog();
            // Logger.LogEditor("DONE - Calculating world chunks", LogCat.PreCaching);
            //
            //
            // var worldChunkData = new StaticCacheService.WorldChunkContainer
            // {
            //     OpaqueChunks = worldChunkCache.MergedChunksByLights[TextureCacheService.TextureArrayTypes.Opaque],
            //     // We do not use transparent elements for OC data as it would cull even on transparent edges.
            //     TransparentChunks = new(),
            //     // We do not use water for OC data
            //     WaterChunks = new()
            // };
            //
            // await new MeshService().CreateWorld(worldChunkData, world.Mesh, null, null, useTextureArray: false).AwaitAndLog();
            // Logger.LogEditor("DONE - Loading world mesh", LogCat.PreCaching);
        }

        private void OnGUI()
        {
            // Do not show Window when game is started.
            if (Application.isPlaying)
            {
                Close();
            }
        }

        private void OnDestroy()
        {
            // FIXME - Needed?
            // _gameStateService.Dispose();
        }
    }
}
