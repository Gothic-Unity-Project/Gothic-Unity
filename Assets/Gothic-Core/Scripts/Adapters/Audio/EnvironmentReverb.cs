using Gothic.Core.Domain.Audio;
using Gothic.Core.Logging;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Audio
{
    /// <summary>
    /// DeveloperConfig.EnableEnvironmentReverb: sounds echo in mines, caves and temples. Gothic set an EAX environment
    /// per zone (zCZoneReverb, not read by ZenKit); V1 measures it: under a ceiling of the world mesh the listener's
    /// reverb zone gets a room's echo - a cave's when the walls are far, a stone corridor's when they're near. Outside
    /// it's off.
    /// </summary>
    public class EnvironmentReverb : MonoBehaviour
    {
        private const float _checkSeconds = 0.5f;
        private const float _wallRayLength = 20f;
        private const float _narrowMeters = 5f;
        private const float _zoneRadius = 1000f;
        private const int _wallRays = 8;

        private AudioReverbZone _zone;
        private AudioListener _listener;
        private float _nextCheckTime;
        private AudioReverbPreset _preset = AudioReverbPreset.Off;

        private void Awake()
        {
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

            if (_listener == null)
                _listener = FindFirstObjectByType<AudioListener>();
            if (_listener == null)
                return;

            var position = _listener.transform.position;
            transform.position = position;

            var preset = GetPreset(position);
            if (preset == _preset)
                return;
            _preset = preset;
            _zone.reverbPreset = preset;
            Logger.Log($"[EnvironmentReverb] {preset}", LogCat.Audio);
        }

        private static AudioReverbPreset GetPreset(Vector3 position)
        {
            if (!GroundSoundDomain.IsUnderWorldCeiling(position))
                return AudioReverbPreset.Off;

            var distance = 0f;
            for (var i = 0; i < _wallRays; i++)
            {
                var direction = Quaternion.Euler(0f, 360f / _wallRays * i, 0f) * Vector3.forward;
                distance += Physics.Raycast(position, direction, out var hit, _wallRayLength,
                    1 << Const.Constants.DefaultLayer, QueryTriggerInteraction.Ignore)
                    ? hit.distance
                    : _wallRayLength;
            }
            return distance / _wallRays < _narrowMeters ? AudioReverbPreset.StoneCorridor : AudioReverbPreset.Cave;
        }
    }
}
