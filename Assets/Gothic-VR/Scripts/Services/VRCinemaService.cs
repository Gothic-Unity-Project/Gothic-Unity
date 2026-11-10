using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Game;
using Gothic.Core.Services.Player;
using Gothic.VR.Adapters.Player;
using Reflex.Attributes;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Plays the videos of GlobalEventDispatcher.PlayVideo (PlayVideo/PlayVideoEx from scripts, logo videos) in a
    /// VRCinema. The cinema lives on the player (Cinema.prefab), several calls in a row (G1 ending: Extro + Credits)
    /// are queued there. The end of the game (quit/restart after the videos) is GameEndingService's, the cinema only
    /// tells it whether a video still runs.
    /// </summary>
    public class VRCinemaService
    {
        [Inject] private readonly VideoService _videoService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly GameEndingService _gameEndingService;

        public void Init()
        {
            GlobalEventDispatcher.PlayVideo.AddListener(Play);
            _gameEndingService.SetVideoRunningCheck(() => GetCinema()?.IsRunning == true);
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
