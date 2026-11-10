using System;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Game
{
    /// <summary>
    /// The end of the game: ExitGame (G1, after the Sleeper) / ExitSession (G2) - once the queued videos (Extro,
    /// Credits) are over, quit or restart (DeveloperConfig.EnableEndingReturnsToMenu). Right away if no video plays.
    /// The platform that plays videos tells whether one is running (SetVideoRunningCheck); without one (Flat) the
    /// game ends right away.
    /// </summary>
    public class GameEndingService
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameLifecycleService _gameLifecycleService;

        private Func<bool> _isVideoRunning;
        private bool _isEndAfterVideos;

        public void Init()
        {
            GlobalEventDispatcher.ScriptExitGame.AddListener(OnScriptExitGame);
            GlobalEventDispatcher.VideoPlaybackFinished.AddListener(OnVideoPlaybackFinished);
        }

        /// <summary>
        /// Set by the platform's video player (VR: the cinema on the player).
        /// </summary>
        public void SetVideoRunningCheck(Func<bool> isVideoRunning)
        {
            _isVideoRunning = isVideoRunning;
        }

        private bool IsVideoRunning => _isVideoRunning?.Invoke() == true;

        private void OnScriptExitGame()
        {
            if (IsVideoRunning)
            {
                _isEndAfterVideos = true;
                return;
            }
            EndGame();
        }

        private void OnVideoPlaybackFinished()
        {
            // A queued video that couldn't be opened reports finished too - the next one still plays.
            if (!_isEndAfterVideos || IsVideoRunning)
                return;
            _isEndAfterVideos = false;
            EndGame();
        }

        private void EndGame()
        {
            // DeveloperConfig.EnableEndingReturnsToMenu: after the ending's videos and credits back to the start (game
            // profiles / main menu) like the engine's ExitSession - the game just closed.
            if (_configService.Dev.EnableEndingReturnsToMenu)
            {
                Logger.Log("Game ended (ExitGame/ExitSession) - back to the main menu.", LogCat.Loading);
                _gameLifecycleService.Restart();
                return;
            }

            Logger.Log("Game ended (ExitGame/ExitSession) - quitting.", LogCat.Loading);
            _gameLifecycleService.Quit();
        }
    }
}
