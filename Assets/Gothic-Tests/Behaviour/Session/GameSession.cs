using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Tests.Behaviour.Session
{
    /// <summary>
    /// The one running game the scenarios share. It boots like the editor does - the Bootstrap scene with the test
    /// config (BootstrapAdapter.ConfigOverride) - once per play session, and is ready when the world is loaded and its
    /// loading screen gone (the surroundings of the spawn are built). Another world is loaded as a new game.
    /// The only place that knows when the game's services can be used.
    /// </summary>
    public static class GameSession
    {
        private const float _loadTimeoutSeconds = 120f;
        private const string _bootstrapScenePath = "Assets/Gothic-Core/Scenes/" + Constants.SceneBootstrap + ".unity";

        /// <summary>
        /// The world is loaded and its surroundings are built: the game's services can be used.
        /// </summary>
        public static bool IsReady { get; private set; }

        /// <summary>
        /// The loaded world, e.g. "world.zen".
        /// </summary>
        public static string World { get; private set; }

        /// <summary>
        /// Seconds the last boot or world load took.
        /// </summary>
        public static float LastLoadSeconds { get; private set; }

        private static bool _isBooted;
        private static bool _isWorldSceneLoaded;


        /// <summary>
        /// Statics outlive a play session when the editor doesn't reload the domain on entering play mode.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsReady = false;
            World = null;
            LastLoadSeconds = 0;
            _isBooted = false;
            _isWorldSceneLoaded = false;
        }

        /// <summary>
        /// The run's game version - Production's. It can't change in a play session: BootstrapService.InitPhase2
        /// mounts it once.
        /// </summary>
        public static GameVersion Version => BehaviourTestConfig.GameVersion;

        /// <summary>
        /// Boots the game into the world, or loads the world as a new game if another one is loaded.
        /// </summary>
        public static IEnumerator Ensure(string world)
        {
            if (IsReady && world.Equals(World, StringComparison.OrdinalIgnoreCase))
                yield break;

            if (_isBooted)
                yield return LoadWorld(world);
            else
                yield return Boot(world);
        }

        /// <summary>
        /// The run is over: put back what the run may have changed on disk.
        /// </summary>
        public static void End()
        {
            GlobalEventDispatcher.WorldSceneLoaded.RemoveListener(OnWorldSceneLoaded);
            if (_isBooted)
                SessionBackup.Restore();
        }

        public static T Resolve<T>()
        {
            if (!IsReady)
                throw new InvalidOperationException($"{typeof(T).Name} asked for before the game is ready");

            return ReflexProjectInstaller.DIContainer.Resolve<T>();
        }

        private static IEnumerator Boot(string world)
        {
            var config = BehaviourTestConfig.Create(world);

            // A backup left by a run that crashed belongs to the developer - it goes back before the game reads it.
            SessionBackup.Restore();
            _isBooted = true;

            Logger.Log($"[BehaviourTests] Booting {Version} into {world}", LogCat.Debug);
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(OnWorldSceneLoaded);
            BootstrapAdapter.ConfigOverride = config;
            SceneManager.LoadScene(_bootstrapScenePath);

            yield return WaitUntilLoaded(world);

            // Booting writes neither, so the backup is made with the game's own paths before the first step changes
            // anything.
            SessionBackup.Create(Resolve<ConfigService>().Gothic.IniFilePath,
                Resolve<SaveGameService>().SavesFolderPath);
        }

        private static IEnumerator LoadWorld(string world)
        {
            BehaviourTestConfig.ToWorldToSpawn(world);
            var worldScene = SceneManager.GetActiveScene().name;
            Logger.Log($"[BehaviourTests] Loading {world} as a new game (was {World})", LogCat.Debug);

            Resolve<BootstrapService>().LoadWorld(world, SaveGameService.SlotId.NewGame, worldScene);
            yield return WaitUntilLoaded(world);
        }

        private static IEnumerator WaitUntilLoaded(string world)
        {
            IsReady = false;
            World = null;
            _isWorldSceneLoaded = false;
            var watch = Stopwatch.StartNew();
            while (!_isWorldSceneLoaded || SceneManager.GetSceneByName(Constants.SceneLoading).isLoaded)
            {
                if (watch.Elapsed.TotalSeconds > _loadTimeoutSeconds)
                    throw new TimeoutException($"{world} wasn't loaded after {_loadTimeoutSeconds} s. Loaded " +
                        $"scenes: {GetLoadedSceneNames()}");

                yield return null;
            }

            IsReady = true;
            var loadedWorld = Resolve<SaveGameService>().CurrentWorldName;
            if (!world.Equals(loadedWorld, StringComparison.OrdinalIgnoreCase))
            {
                IsReady = false;
                throw new InvalidOperationException($"Asked for {world}, the game loaded {loadedWorld}");
            }

            World = loadedWorld;
            LastLoadSeconds = (float)watch.Elapsed.TotalSeconds;
            Logger.Log($"[BehaviourTests] {world} is ready after {LastLoadSeconds:0.0} s", LogCat.Debug);
        }

        private static string GetLoadedSceneNames()
        {
            return string.Join(", ", Enumerable.Range(0, SceneManager.sceneCount)
                .Select(i => SceneManager.GetSceneAt(i).name));
        }

        private static void OnWorldSceneLoaded()
        {
            _isWorldSceneLoaded = true;
        }
    }
}
