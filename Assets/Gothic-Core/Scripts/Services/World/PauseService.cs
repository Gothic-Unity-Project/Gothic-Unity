using Gothic.Core.Logging;
using Gothic.Core.Models.UI;
using Gothic.Core.Services.Audio;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// Pauses the world like Gothic while the menu is open or a video plays: time stands still (Time.timeScale 0), so
    /// NPCs, fights, knockouts and the clock stop, and the world's sounds pause. Several reasons can hold the pause at
    /// once; the world runs again once all of them resumed. The music keeps playing in the menu, not in a video.
    /// </summary>
    public class PauseService
    {
        [Inject] private readonly AudioService _audioService;

        private float _unpausedTimeScale = 1f;

        public PauseReasons ActiveReasons { get; private set; }

        public bool IsPaused => ActiveReasons != PauseReasons.None;

        public void Pause(PauseReasons reason)
        {
            if ((ActiveReasons & reason) == reason)
                return;

            if (!IsPaused)
                _unpausedTimeScale = Time.timeScale;

            ActiveReasons |= reason;
            Apply();
        }

        /// <summary>
        /// Ends the pause of these reasons (several at once as flags). Other reasons keep the world paused.
        /// </summary>
        public void Resume(PauseReasons reasons)
        {
            if ((ActiveReasons & reasons) == PauseReasons.None)
                return;

            ActiveReasons &= ~reasons;
            Apply();
        }

        private void Apply()
        {
            Time.timeScale = IsPaused ? 0f : _unpausedTimeScale;
            AudioListener.pause = IsPaused;
            _audioService.SetMusicPlaysWhilePaused(ActiveReasons == PauseReasons.Menu);
            Logger.Log($"World pause: {ActiveReasons}", LogCat.Misc);
        }
    }
}
