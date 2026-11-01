using System.Diagnostics;
using System.Threading.Tasks;
using Gothic.Core.Logging;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// Builds the VOBs and NPCs around a place the player just arrived at - after loading a world, after a teleport -
    /// so they don't pop in around him. Call it while the view is hidden: the queues may use long frames meanwhile
    /// (FrameSkipperService.IsViewHidden). Whatever is still missing after the wait appears in the background.
    /// </summary>
    public class SurroundingsService
    {
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly FrameSkipperService _frameSkipperService;

        // VOBs and NPCs this close to the player are built before the view is shown (m). Lights reach the ground up to
        // StationaryLightsService's 80 m, but the near ones matter most and keep the black short.
        public const float Radius = 50f;

        // Longest wait for them (s). Whatever is still missing then appears while the player looks around.
        private const float _maxWait = 5f;

        // The camera counts as arrived this close (m, horizontal; VR play area offset included).
        private const float _cameraArrivalDistance = 5f;
        private const float _cameraArrivalMaxWait = 1f; // s

        // Frames culling needs after the camera arrived to queue the objects around it.
        private const int _cullingUpdateFrames = 2;


        /// <summary>
        /// Waits until the queued VOBs and NPCs within Radius of center are built, nearest first. With half a frame
        /// per frame (the default), a crowded place like the Old Camp popped in over several seconds.
        /// </summary>
        /// <returns>False if the wait ended before everything near was built.</returns>
        public async Task<bool> BuildAroundAsync(Vector3 center)
        {
            var watch = Stopwatch.StartNew();
            var isBuilt = true;
            _frameSkipperService.IsViewHidden = true;
            _vobService.SetInitFocus(center, Radius);
            _npcService.SetInitFocus(center, Radius);
            try
            {
                // The VR rig applies a teleport in its next update, so the camera may still be at the old place.
                // Culling queues the VOBs and NPCs around the new one once the camera is there.
                while (!IsCameraNear(center) && watch.Elapsed.TotalSeconds < _cameraArrivalMaxWait)
                    await Task.Yield();
                for (var i = 0; i < _cullingUpdateFrames; i++)
                    await Task.Yield();

                while (IsBuilding(center))
                {
                    if (watch.Elapsed.TotalSeconds >= _maxWait)
                    {
                        Logger.LogWarning($"Surroundings not built after {_maxWait} s. " +
                                          "The rest appears in the background.", LogCat.Loading);
                        isBuilt = false;
                        break;
                    }

                    await Task.Yield();
                }
            }
            finally
            {
                _frameSkipperService.IsViewHidden = false;
                _vobService.ClearInitFocus();
                _npcService.ClearInitFocus();
            }

            Logger.Log($"Surroundings within {Radius} m built after {watch.Elapsed.TotalSeconds:0.00} s.",
                LogCat.Loading);
            return isBuilt;
        }

        private bool IsBuilding(Vector3 center)
        {
            return _vobService.HasQueuedVobWithin(center, Radius) || _npcService.HasQueuedNpcWithin(center, Radius);
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
