using System.Diagnostics;
using System.Globalization;
using System.IO;
using Gothic.Core.Adapters.Scenes;
using Gothic.Core.Const;
using Gothic.Core.Domain;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Config;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.StaticCache;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.Core.Extensions;
using MyBox;
using Reflex.Attributes;
using UnityEngine.SceneManagement;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services
{
    public class BootstrapService
    {

        private FileLoggingHandler _fileLoggingHandler;


        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly SpeechToTextService _speechToTextService;
        [Inject] private readonly GameTimeService _gameTimeService;
        
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        [Inject] private readonly VobSoundCullingService _vobSoundCullingService;
        
        [Inject] private readonly TextureService _textureService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly PlayerService _playerService;
        
        [Inject] private readonly FrameSkipperService _frameSkipperService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcRoutineService _npcRoutineService;
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly ParticleService _particleService;
        
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly StaticCacheService _staticCacheService;


        private BootstrapDomain _bootstrapDomain = new BootstrapDomain().Inject();
        
        public void AwakeUnity(DeveloperConfig config)
        {
            // We need to set culture to this, otherwise e.g. polish numbers aren't parsed correct.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            _configService.LoadRootJson();
            _configService.SetDeveloperConfig(config);

            _fileLoggingHandler = new FileLoggingHandler();

            _multiTypeCacheService.Init();

            ZenKit.Logger.Set(_configService.Dev.ZenKitLogLevel, Logger.OnZenKitLogMessage);
            DirectMusic.Logger.Set(_configService.Dev.DirectMusicLogLevel, Logger.OnDirectMusicLogMessage);

            _fileLoggingHandler.Init(_configService.Root);
        }

        public void StartUnity()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            // Call init function of BootstrapSceneManager directly as it kicks off cleaning up of further loaded scenes.
            SceneManager.GetActiveScene().GetComponentInChildren<BootstrapScene>()!.Init();
        }

        /// <summary>
        /// Init when game starts and Controls are set already, but no Gothic game version is selected so far.
        /// </summary>
        public void InitPhase1()
        {
            GlobalEventDispatcher.RegisterControlsService.Invoke(_configService.Dev.GameControls);

            _frameSkipperService.Init();
            _vobMeshCullingService.Init();
            _npcMeshCullingService.Init();
            _vobSoundCullingService.Init();
            _gameTimeService.Init();
            _saveGameService.Init();
            _npcRoutineService.Init();
            _fightService.Init();
            _particleService.Init();
        }

        /// <summary>
        /// Once Gothic version is selected, we can now initialize remaining managers.
        /// </summary>
        public void InitPhase2(GameVersion version)
        {
            var watch = Stopwatch.StartNew();

            GlobalEventDispatcher.RegisterGameVersionService.Invoke(version);

            _configService.LoadGothicInis(version);

            var gothicRootPath = _contextGameVersionService.RootPath;

            Logger.Log($"Initializing Gothic installation at: {gothicRootPath}", LogCat.Loading);
            _resourceCacheService.Init(gothicRootPath);

            _audioService.InitMusic();
            _staticCacheService.Init();
            _textureService.Init();
            _vobService.Init();
            _npcService.Init();

            _bootstrapDomain.Boot();
            _speechToTextService.Init(); // Init after language set.

            GlobalEventDispatcher.LevelChangeTriggered.AddListener((world, spawn) =>
            {
                _playerService.LastLevelChangeTriggerVobName = spawn;
                LoadWorld(world, SaveGameService.SlotId.WorldChangeOnly, SceneManager.GetActiveScene().name);
            });

            watch.Log("Phase2 done. (mostly ZenKit initialized)");
        }

        public void LoadScene(string sceneName, string unloadScene = null)
        {
            if (unloadScene.NotNullOrEmpty())
            {
                SceneManager.UnloadSceneAsync(SceneManager.GetSceneByName(unloadScene));
            }

            // Fallback world loading without OC.
            // FIXME - needs to be solved better for performance reasons and dynamic support of Mods.
            if (SceneUtility.GetBuildIndexByScenePath(sceneName) == -1)
            {
                Logger.LogWarning($"Scene {sceneName} not found. Falling back to {Constants.SceneDefaultWorld} without Occlusion Culling.", LogCat.Loading);
                sceneName = Constants.SceneDefaultWorld;
            }

            SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        }

        /// <summary>
        /// Gothic saves start with number 1
        /// saveGameId = 0 -> New Game
        /// saveGameId = -1 -> Change World
        /// </summary>
        /// <param name="saveGameId">-1-15</param>
        public void LoadWorld(string worldName, SaveGameService.SlotId saveGameId, string sceneToUnload = null)
        {
            // We need to add .zen as early as possible as all related data needs the file ending.
            worldName += worldName.EndsWithIgnoreCase(".zen") ? "" : ".zen";

            // Pre-load ZenKit save game data now. Can be reused by LoadingSceneManager later.
            if (saveGameId == SaveGameService.SlotId.NewGame)
            {
                _saveGameService.LoadNewGame();
            }
            else if (saveGameId > 0)
            {
                _saveGameService.LoadSavedGame(saveGameId);
            }
            else
            {
                // World change triggers (level transitions) bypass SaveGameService, so LoadGameStart
                // is never fired and culling domains don't get PreWorldCreate. Reset them manually.
                _saveGameService.ClearPendingNpcData();
                _vobService.PreWorldCreate();
                _vobMeshCullingService.PreWorldCreate();
                _npcMeshCullingService.PreWorldCreate();
                _vobSoundCullingService.PreWorldCreate();
                _npcService.ClearQueues();
            }
            _saveGameService.ChangeWorld(worldName);

            LoadScene(Constants.SceneLoading, sceneToUnload);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.Log($"Scene loaded: {scene.name}", LogCat.Loading);

            // Newly created scenes are always the ones which we set as main scenes (i.e. new GameObjects will spawn in here automatically)
            SceneManager.SetActiveScene(scene);

            var sceneManager = scene.GetComponentInChildren<IScene>();
            if (sceneManager == null)
            {
                Logger.LogError($"{nameof(IScene)} for scene >{scene.name}< not found. Game won't proceed as " +
                                "bootstrapper for scene is invalid/non-existent.", LogCat.Loading);
                return;
            }
            sceneManager.Init();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            Logger.Log($"Scene unloaded: {scene.name}", LogCat.Loading);
        }

        public void OnDestroy()
        {
            _fileLoggingHandler.Destroy();
        }
    }
}
