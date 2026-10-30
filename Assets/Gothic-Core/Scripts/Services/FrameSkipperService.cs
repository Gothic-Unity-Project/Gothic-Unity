using System.Collections;
using System.Threading.Tasks;
using Gothic.Core.Logging;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services
{
    /// <summary>
    /// Leverage this class to check if an Async operation can continue or should wait until next frame.
    /// Idea is to have enough time for Unity's main thread to also execute other Update() calls, physics, etc.
    ///
    /// Hint: We set ScriptExecutionOrder to something early on to ensure we fetch frame time early on.
    /// </summary>
    public class FrameSkipperService
    {
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        
        // We will leverage 50% (0.5f) of a frame time only.
        private const float _frameQuota = 0.5f;

        // While the view is hidden (black after a world load), nobody sees a long frame. Fewer, longer frames leave
        // more of the time to the queued work instead of the per-frame work of the rest of the game.
        private const float _hiddenViewFrameQuota = 3f;

        private static float _targetFrameRate;
        private static float _frameStart;

        /// <summary>
        /// Lets queued work use about three frames per frame instead of half a frame. Only while the player can't see
        /// the world.
        /// </summary>
        public bool IsViewHidden { get; set; }

        /// <summary>
        /// Whether this frame's share for queued work is used up.
        /// </summary>
        public bool IsFrameBudgetUsed =>
            Time.realtimeSinceStartup - _frameStart >
            (IsViewHidden ? _hiddenViewFrameQuota : _frameQuota) / _targetFrameRate;


        /// <summary>
        /// We need to set the values via Init() to ensure LogFileHandler is set before our class inside GameManager.Init().
        /// </summary>
        public void Init()
        {
            _targetFrameRate = _contextInteractionService.GetFrameRate();

            if (_targetFrameRate <= 0)
            {
                // We assume a default when no data is available.
                _targetFrameRate = 60;

                // Ite means there is some error in detecting the frame rate in a production build.
                if (!Application.isEditor)
                {
                    Logger.LogError($"[{typeof(FrameSkipperService)}] No target frame rate found.", LogCat.Loading);
                }
            }
            Logger.Log($"[{typeof(FrameSkipperService)}] Target frame rate set to: {_targetFrameRate}", LogCat.Loading);
        }


        /// <summary>
        /// Every update cycle, we calculate when it started and when our threshold of _skip to next frame_ is reached.
        /// </summary>
        public void Update()
        {
            // _debugSkippedCount = 0;
            _frameStart = Time.realtimeSinceStartup;
        }

        // private static int _debugSkippedCount;
        public async Task TrySkipToNextFrame()
        {
            if (IsFrameBudgetUsed)
            {
                // Logger.Log($"FrameSkipper: Not skipped {_debugSkippedCount}x.");
                await Task.Yield();
            }
            else
            {
                // _debugSkippedCount++;
            }
        }

        public IEnumerator TrySkipToNextFrameCoroutine()
        {
            if (IsFrameBudgetUsed)
            {
                // Logger.Log($"FrameSkipper: Not skipped {_debugSkippedCount}x.");
                yield return null;
            }
            else
            {
                // _debugSkippedCount++;
            }

        }
    }
}
