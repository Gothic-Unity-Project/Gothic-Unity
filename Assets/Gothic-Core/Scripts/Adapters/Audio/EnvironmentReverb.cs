using Gothic.Core.Const;
using Gothic.Core.Domain.Audio;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Audio
{
    /// <summary>
    /// Sounds echo in mines, caves and temples. Gothic set an EAX environment per zone (zCZoneReverb, not read by
    /// ZenKit); this measures it instead: under a ceiling of the world mesh the reverb zone around the listener gets a
    /// room's echo - a cave's when the walls are far, a stone corridor's when they're near. Outside it's off.
    /// On Rain.prefab, which follows the player's head (RainFollower) in VR and Flat.
    /// </summary>
    public class EnvironmentReverb : MonoBehaviour
    {
        private const float _checkSeconds = 0.5f;
        private const float _wallRayLength = 20f;
        private const float _narrowMeters = 5f;
        // The zone moves with the head - big enough to always contain the listener.
        private const float _zoneRadius = 1000f;
        private const int _wallRays = 8;

        [Inject] private readonly VobService _vobService;

        private AudioReverbZone _zone;
        private float _nextCheckTime;
        private AudioReverbPreset _preset = AudioReverbPreset.Off;

        private void Awake()
        {
            this.Inject();
            _zone = gameObject.AddComponent<AudioReverbZone>();
            _zone.minDistance = _zoneRadius;
            _zone.maxDistance = _zoneRadius;
            _zone.reverbPreset = AudioReverbPreset.Off;
        }

        private void Update()
        {
            if (Time.time < _nextCheckTime)
                return;
            _nextCheckTime = Time.time + _checkSeconds;

            var preset = GetPreset(transform.position);
            if (preset == _preset)
                return;

            _preset = preset;
            _zone.reverbPreset = preset;
            Logger.Log($"[EnvironmentReverb] {preset}", LogCat.Audio);
        }

        private AudioReverbPreset GetPreset(Vector3 position)
        {
            if (!GroundSoundDomain.IsUnderWorldCeiling(position, _vobService.IsVobCollider))
                return AudioReverbPreset.Off;

            var distance = 0f;
            for (var i = 0; i < _wallRays; i++)
            {
                var direction = Quaternion.Euler(0f, 360f / _wallRays * i, 0f) * Vector3.forward;
                distance += Physics.Raycast(position, direction, out var hit, _wallRayLength,
                    1 << Constants.DefaultLayer, QueryTriggerInteraction.Ignore)
                    ? hit.distance
                    : _wallRayLength;
            }

            return distance / _wallRays < _narrowMeters ? AudioReverbPreset.StoneCorridor : AudioReverbPreset.Cave;
        }
    }
}
