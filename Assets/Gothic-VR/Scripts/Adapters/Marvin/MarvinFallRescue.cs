#if GOTHIC_HVR_INSTALLED
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Marvin
{
    /// <summary>
    /// DeveloperConfig.EnableMarvinFallRescue (Marvin mode only): fell out of the world (e.g. after Marvin's "TP +4 m"
    /// through a wall) - no floor below the hero for a while - back to the last spot the hero stood on, else to the
    /// world's start point (START / START_GOTHIC2, like entering the world). Mroczne Tajemnice's abandoned mine has its
    /// START over the void (it is entered through a portal) - the rescue sent the hero there again and again.
    /// </summary>
    public class MarvinFallRescue : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly MarvinService _marvinService;

        private const float _noFloorSeconds = 2f;
        private const float _floorSearchDistance = 200f;
        private float _noFloorTime;
        private const float _standingDistance = 2f;
        private const float _safeSpotInterval = 0.5f;
        private float _nextSafeSpotTime;
        private Vector3? _lastSafeSpot;
        private Quaternion _lastSafeRotation;

        private void Awake() => this.Inject();

        private void Update()
        {
            if (!_marvinService.IsMarvinMode || !_configService.Dev.EnableMarvinFallRescue)
                return;

            var player = _contextInteractionService.GetCurrentPlayerController();
            if (player == null)
                return;

            var feet = player.transform.position;
            if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out var hit, _floorSearchDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                _noFloorTime = 0f;
                // Standing on something: remember it as the place to come back to.
                if (hit.distance <= _standingDistance && Time.unscaledTime >= _nextSafeSpotTime)
                {
                    _nextSafeSpotTime = Time.unscaledTime + _safeSpotInterval;
                    _lastSafeSpot = hit.point;
                    _lastSafeRotation = player.transform.rotation;
                }
                return;
            }

            _noFloorTime += Time.unscaledDeltaTime;
            if (_noFloorTime < _noFloorSeconds)
                return;

            _noFloorTime = 0f;
            if (_lastSafeSpot.HasValue)
            {
                Logger.Log($"[MarvinFallRescue] No floor below the hero for {_noFloorSeconds:F0} s at {feet} - back to " +
                           $"the last spot stood on {_lastSafeSpot.Value}", LogCat.Ui);
                _contextInteractionService.TeleportPlayerTo(_lastSafeSpot.Value + Vector3.up * 0.2f, _lastSafeRotation);
                return;
            }

            var start = _gameStateService.FreePoints.FirstOrDefault(fp =>
                fp.Key.EqualsIgnoreCase("START") || fp.Key.EqualsIgnoreCase("START_GOTHIC2"));
            if (start.Value == null)
                start = _gameStateService.FreePoints.FirstOrDefault(fp =>
                    fp.Key.StartsWith("START", System.StringComparison.OrdinalIgnoreCase));
            if (start.Value == null)
            {
                Logger.LogWarning("[MarvinFallRescue] Fell out of the world, but the world has no START point.", LogCat.Ui);
                return;
            }

            Logger.Log($"[MarvinFallRescue] No floor below the hero for {_noFloorSeconds:F0} s at {feet} - back to " +
                       $"'{start.Key}'", LogCat.Ui);
            _contextInteractionService.TeleportPlayerTo(start.Value.Position, start.Value.Rotation);
        }
    }
}
#endif
