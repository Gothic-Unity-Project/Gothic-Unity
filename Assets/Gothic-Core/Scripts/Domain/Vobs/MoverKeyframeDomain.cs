using UnityEngine;

namespace Gothic.Core.Domain.Vobs
{
    /// <summary>
    /// Keyframe rules of zCMover. Pure math - the MoverAdapter does the moving.
    /// </summary>
    public static class MoverKeyframeDomain
    {
        /// <summary>
        /// A VOB this close to its key (m squared) stands on it.
        /// </summary>
        private const float _sameKeyPositionSqr = 0.0001f;

        /// <summary>
        /// A VOB rotated less than this (degrees) against its key stands on it.
        /// </summary>
        private const float _sameKeyDegrees = 1f;

        /// <summary>
        /// The engine moves a mover by its keyframes relative to where the VOB stands (OpenGothic MoveTrigger:
        /// pos0 = vob * inverse(keyframe[0]), shown at pos0 * keyframe[i]). G2 Irdorath's gates stand away from their
        /// keyframes. Our saves keep the VOB's world file position (key 0). A Gothic save game stores where the mover
        /// stands - on a key it reached or between two keys (actKeyframeF, saved while moving), nothing to do.
        /// Changes the keyframes in place. Returns false if the VOB already stands on its keys.
        /// </summary>
        public static bool RelativeToVob((Vector3 pos, Quaternion rot)[] keyframes, Vector3 vobPosition,
            Quaternion vobRotation, int actKeyframe, float actKeyframeF, out Vector3 positionOffset,
            out float rotationAngle)
        {
            positionOffset = Vector3.zero;
            rotationAngle = 0f;
            if (keyframes.Length == 0)
                return false;

            if (actKeyframe > 0 && actKeyframe < keyframes.Length &&
                (vobPosition - keyframes[actKeyframe].pos).sqrMagnitude < _sameKeyPositionSqr)
                return false;

            if (IsBetweenKeys(keyframes, vobPosition, actKeyframeF))
                return false;

            var (keyPosition, keyRotation) = keyframes[0];
            var rotationOffset = vobRotation * Quaternion.Inverse(keyRotation);
            positionOffset = vobPosition - keyPosition;
            rotationAngle = Quaternion.Angle(rotationOffset, Quaternion.identity);
            if (positionOffset.sqrMagnitude < _sameKeyPositionSqr && rotationAngle < _sameKeyDegrees)
                return false;

            // The rotation conversions of keyframes and VOB differ slightly - only a real difference turns them.
            if (rotationAngle < _sameKeyDegrees)
                rotationOffset = Quaternion.identity;

            for (var i = 0; i < keyframes.Length; i++)
            {
                var (position, rotation) = keyframes[i];
                keyframes[i] = (vobPosition + rotationOffset * (position - keyPosition), rotationOffset * rotation);
            }

            return true;
        }

        /// <summary>
        /// Whether the VOB stands where the mover was saved while moving: at actKeyframeF, between its two keys.
        /// </summary>
        private static bool IsBetweenKeys((Vector3 pos, Quaternion rot)[] keyframes, Vector3 vobPosition,
            float actKeyframeF)
        {
            if (actKeyframeF <= 0f || actKeyframeF >= keyframes.Length)
                return false;

            var fromKey = Mathf.FloorToInt(actKeyframeF);
            var toKey = (fromKey + 1) % keyframes.Length;
            var position = Vector3.Lerp(keyframes[fromKey].pos, keyframes[toKey].pos, actKeyframeF - fromKey);
            return (vobPosition - position).sqrMagnitude < _sameKeyPositionSqr;
        }
    }
}
