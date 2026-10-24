using System;
using System.Collections.Generic;
using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// Minimal 3D one-shot player via AudioSource.PlayClipAtPoint (fully spatial, default rolloff, no mixer group).
    /// HVR's pool used a reference AudioSource from HVRGlobal.prefab; an own pool with the same settings is a later
    /// phase.
    /// </summary>
    public class XriSfxPlayer : IVrSfxPlayer
    {
        private readonly Dictionary<Guid, float> _lastPlayTimes = new();

        public void Play(AudioClip clip, Vector3 position)
        {
            if (clip == null)
                return;

            AudioSource.PlayClipAtPoint(clip, position);
        }

        public void PlayWithCooldown(AudioClip clip, Vector3 position, Guid sourceId, float cooldownTime)
        {
            if (_lastPlayTimes.TryGetValue(sourceId, out var lastPlayTime) && Time.time - lastPlayTime < cooldownTime)
                return;

            _lastPlayTimes[sourceId] = Time.time;
            Play(clip, position);
        }
    }
}
