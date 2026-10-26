using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Player;
using Gothic.VR.Adapters.Player;
using Reflex.Attributes;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Plays the videos of GlobalEventDispatcher.PlayVideo (PlayVideo/PlayVideoEx from scripts, logo videos) in a
    /// VRCinema. The cinema lives on the player (Cinema.prefab), several calls in a row (G1 ending: Extro + Credits)
    /// are queued there.
    /// </summary>
    public class VRCinemaService
    {
        [Inject] private readonly VideoService _videoService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly ConfigService _configService;

        private bool _isQuitAfterVideos;

        public void Init()
        {
            GlobalEventDispatcher.PlayVideo.AddListener(Play);
            GlobalEventDispatcher.ScriptExitGame.AddListener(QuitAfterVideos);
            GlobalEventDispatcher.VideoPlaybackFinished.AddListener(OnVideoPlaybackFinished);
        }

        /// <summary>
        /// ExitGame/ExitSession (ending): quit once the queued videos (Extro, Credits) are over - right away if none play.
        /// </summary>
        private void QuitAfterVideos()
        {
            if (GetCinema()?.IsRunning == true)
            {
                _isQuitAfterVideos = true;
                return;
            }
            Quit();
        }

        private void OnVideoPlaybackFinished()
        {
            if (!_isQuitAfterVideos)
                return;
            _isQuitAfterVideos = false;
            Quit();
        }

        private void Quit()
        {
            // DeveloperConfig.EnableEndingReturnsToMenu: after the ending's videos and credits back to the start (game
            // profiles / main menu) like the engine's ExitSession - the game just closed.
            if (_configService.Dev.EnableEndingReturnsToMenu)
            {
                Logger.Log("[VRCinema] Game ended (ExitGame/ExitSession) - back to the main menu.", LogCat.VR);
                Gothic.Core.Domain.GameRestartDomain.Restart();
                return;
            }

            Logger.Log("[VRCinema] Game ended (ExitGame/ExitSession) - quitting.", LogCat.VR);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }

        private Adapters.UI.VRCinema GetCinema()
        {
            var playerController = _contextInteractionService.GetCurrentPlayerController();
            return playerController == null ? null : playerController.GetComponent<VRPlayer>().Cinema;
        }

        public void Play(string fileName)
        {
            var path = _videoService.FindVideo(fileName);
            if (path == null)
            {
                Logger.LogWarning($"[VRCinema] '{fileName}' not found in _work/DATA/video/ - skipped.",
                    LogCat.VR);
                return;
            }

            var cinema = GetCinema();
            if (cinema == null)
            {
                Logger.LogWarning($"[VRCinema] No cinema on the player - '{fileName}' skipped.", LogCat.VR);
                // Waiting scenes (Logo) continue nevertheless.
                GlobalEventDispatcher.VideoPlaybackFinished.Invoke();
                return;
            }

            cinema.Enqueue(path);
        }
    }
}
