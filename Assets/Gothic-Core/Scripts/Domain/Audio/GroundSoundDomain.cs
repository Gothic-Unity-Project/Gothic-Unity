using System;
using Gothic.Core.Const;
using UnityEngine;

namespace Gothic.Core.Domain.Audio
{
    /// <summary>
    /// Footsteps (Gothic.ini [SOUND] footstepSounds): ground sound events and the world's ceiling.
    /// </summary>
    public static class GroundSoundDomain
    {
        // Marks a ground sound event (*eventSFXGrnd) among the sound events (AnimationService) - GroundSoundService adds
        // the ground's material when it plays.
        public const string GroundEventPrefix = "#GRND#";

        private const float _ceilingRayStart = 1f;
        private const float _ceilingRayLength = 15f;

        /// <summary>
        /// The world mesh above (not a VOB: trees and roofs on poles don't make a cave) - mines, caves, temples.
        /// </summary>
        public static bool IsUnderWorldCeiling(Vector3 position, Func<Collider, bool> isVobCollider)
        {
            return Physics.Raycast(position + Vector3.up * _ceilingRayStart, Vector3.up, out var hit, _ceilingRayLength,
                       1 << Constants.DefaultLayer, QueryTriggerInteraction.Ignore) &&
                   !isVobCollider(hit.collider);
        }
    }
}
