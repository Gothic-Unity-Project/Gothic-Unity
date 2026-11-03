using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Gothic.Core.Domain.Culling;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// Builds the VOBs and NPCs around a point (spawn point, level change, save game) while the loading screen is
    /// still shown, so they don't pop in when the view is shown.
    /// </summary>
    public class SurroundingsBuildService
    {
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly LoadingService _loadingService;
        [Inject] private readonly FrameSkipperService _frameSkipperService;

        // VOBs and NPCs this close to the spawn point are built before the view is shown (m). Lights reach the ground
        // up to StationaryLightsService's 80 m, but the near ones matter most and keep the wait short.
        private const float _surroundingsRadius = 50f;

        // Longest wait for them (s). Whatever is still missing then appears while the player looks around.
        private const float _surroundingsMaxWait = 5f;

        // The camera counts as arrived at the spawn point this close (m, horizontal; VR play area offset included).
        private const float _cameraArrivalDistance = 5f;
        private const float _cameraArrivalMaxWait = 1f; // s

        // Frames culling needs after the camera arrived to queue the objects around it.
        private const int _cullingUpdateFrames = 2;


        /// <summary>
        /// Waits until the queued VOBs and NPCs around center are built. Only the loading screen is visible
        /// meanwhile, its bar (loadingPhase) shows how many of them are built. So the queues may use long frames
        /// (FrameSkipperService.UseLoadingBudget). With half a frame per frame (the default), a crowded place like
        /// the Old Camp popped in over several seconds.
        /// </summary>
        public async Task BuildAround(Vector3 center, string loadingPhase)
        {
            var watch = Stopwatch.StartNew();
            _loadingService.SetPhase(loadingPhase, 1);
            var lazyInitQueues = new ILazyInitQueue[] { _vobService, _npcService };
            foreach (var queue in lazyInitQueues)
                queue.SetInitFocus(center, _surroundingsRadius);
            using var loadingBudget = _frameSkipperService.UseLoadingBudget();
            try
            {
                // The VR rig applies a teleport in its next update, so the camera is still at the loading screen
                // here. Culling queues the VOBs and NPCs around the spawn point once the camera is there.
                while (!IsCameraNear(center) && watch.Elapsed.TotalSeconds < _cameraArrivalMaxWait)
                    await Task.Yield();
                for (var i = 0; i < _cullingUpdateFrames; i++)
                    await Task.Yield();

                // Culling still queues objects while the first ones are built - the bar never goes back.
                var mostWaiting = 0;
                var progress = 0f;
                while (true)
                {
                    var waiting = lazyInitQueues.Sum(queue => queue.CountQueuedWithin(center, _surroundingsRadius));
                    if (waiting == 0)
                        break;

                    mostWaiting = Math.Max(mostWaiting, waiting);
                    progress = Math.Max(progress, 1f - (float)waiting / mostWaiting);
                    _loadingService.SetPhaseProgress(progress);

                    if (watch.Elapsed.TotalSeconds >= _surroundingsMaxWait)
                    {
                        Logger.LogWarning($"Surroundings not built after {_surroundingsMaxWait} s. " +
                                          "The rest appears in the background.", LogCat.Loading);
                        break;
                    }

                    await Task.Yield();
                }
            }
            finally
            {
                foreach (var queue in lazyInitQueues)
                    queue.ClearInitFocus();
                _loadingService.FinalizePhase();
            }

            Logger.Log($"Surroundings within {_surroundingsRadius} m built after {watch.Elapsed.TotalSeconds:0.00} s.",
                LogCat.Loading);
        }

        private static bool IsCameraNear(Vector3 position)
        {
            var camera = Camera.main;
            if (camera == null)
                return false;

            var offset = camera.transform.position - position;
            offset.y = 0;
            return offset.sqrMagnitude < _cameraArrivalDistance * _cameraArrivalDistance;
        }
    }
}
