using System;
using System.Diagnostics;
using System.IO;
using Gothic.Core.Logging;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gothic.Core.Services.Game
{
    /// <summary>
    /// Restarts or quits the game process - used after the end of the game (GameEndingService).
    /// A restart is a fresh start that shows the game profile list again (if enabled).
    /// Editor: stops Play mode and enters it again. Standalone: starts the exe again and quits this one.
    /// The game can't switch Gothic version/mod in place - ZenKit, the VM and caches are set up once per start.
    /// </summary>
    public class GameLifecycleService
    {
        private const string _dataFolderSuffix = "_Data";
#if UNITY_EDITOR
        private const string _replayKey = "Gothic.RestartGame.EnterPlayMode";

        /// <summary>
        /// Editor hook (static, outside DI): enters Play mode again once Restart() stopped it.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void RegisterEditorReplay()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(_replayKey, false))
                return;

            SessionState.SetBool(_replayKey, false);
            EditorApplication.isPlaying = true;
        }
#endif

        public void Restart()
        {
            Logger.Log("Restarting the game.", LogCat.Loading);
#if UNITY_EDITOR
            SessionState.SetBool(_replayKey, true);
            EditorApplication.isPlaying = false;
#else
            if (Application.isMobilePlatform)
            {
                // A headset app can't start itself again - quitting is the closest we get.
                Logger.LogWarning("Restart isn't possible on this platform - quitting instead.", LogCat.Loading);
                Application.Quit();
                return;
            }

            // Standalone layout: <Name>.exe next to <Name>_Data (= Application.dataPath).
            var dataPath = Application.dataPath;
            var exePath = dataPath.EndsWith(_dataFolderSuffix)
                ? dataPath.Substring(0, dataPath.Length - _dataFolderSuffix.Length) + ".exe"
                : null;

            if (exePath == null || !File.Exists(exePath))
            {
                Logger.LogError($"Restart failed: game exe not found next to >{dataPath}<.", LogCat.Loading);
                return;
            }

            try
            {
                Process.Start(exePath);
            }
            catch (Exception e)
            {
                Logger.LogError($"Restart failed: >{exePath}< didn't start: {e.Message}", LogCat.Loading);
                return;
            }
            Application.Quit();
#endif
        }

        /// <summary>
        /// Closes the game. Editor: stops Play mode.
        /// </summary>
        public void Quit()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
