#if GOTHIC_HVR_INSTALLED
using System;
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.Core.Utils;
using UnityEngine;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Uses HVR's SFXPlayer pool (HVRGlobal.prefab) until we have our own.
    /// </summary>
    public class HvrSfxPlayer : IVrSfxPlayer
    {
        public void Play(AudioClip clip, Vector3 position)
        {
            SFXPlayer.Instance.PlaySFX(clip, position);
        }

        public void PlayWithCooldown(AudioClip clip, Vector3 position, Guid sourceId, float cooldownTime)
        {
            SFXPlayer.Instance.PlaySFXCooldown(clip, position, sourceId, cooldownTime: cooldownTime);
        }
    }
}
#endif
