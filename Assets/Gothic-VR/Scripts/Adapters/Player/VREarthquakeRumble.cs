#if GOTHIC_HVR_INSTALLED
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.VR.Services;
using Gothic.VR.Interaction.Models;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVisualFxEarthquake: Gothic shakes the camera for FX_EarthQuake (summoning, fire rain) - in
    /// VR that's sickening, so both controllers rumble instead while the hero is within its radius.
    /// </summary>
    public class VREarthquakeRumble : MonoBehaviour
    {
        [Inject] private readonly VrHapticsService _hapticsService;

        private const float _pulseSeconds = 0.25f;

        private float _until;
        private float _nextPulse;

        private void Awake()
        {
            this.Inject();
            GlobalEventDispatcher.VisualFxEarthquake.AddListener(OnEarthquake);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.VisualFxEarthquake.RemoveListener(OnEarthquake);
        }

        private void OnEarthquake(Vector3 position, float radius, float seconds)
        {
            var head = Camera.main;
            if (head == null || Vector3.Distance(head.transform.position, position) > radius)
                return;
            _until = Mathf.Max(_until, Time.time + seconds);
            Logger.Log($"[EarthquakeRumble] {seconds:F1} s", LogCat.VR);
        }

        private void Update()
        {
            if (Time.time >= _until || Time.time < _nextPulse)
                return;
            _nextPulse = Time.time + _pulseSeconds;
            _hapticsService.Vibrate(VrHandSide.Left, VrHapticPreset.Warning);
            _hapticsService.Vibrate(VrHandSide.Right, VrHapticPreset.Warning);
        }
    }
}
#endif
