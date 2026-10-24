using System;
using UnityEngine;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// Fire-and-forget 3D sounds of the VR player (weapon swings, swimming) from a pool of audio sources.
    /// </summary>
    public interface IVrSfxPlayer
    {
        void Play(AudioClip clip, Vector3 position);

        /// <summary>
        /// Plays only if no sound with the same sourceId was started within the last cooldownTime seconds.
        /// </summary>
        void PlayWithCooldown(AudioClip clip, Vector3 position, Guid sourceId, float cooldownTime);
    }
}
