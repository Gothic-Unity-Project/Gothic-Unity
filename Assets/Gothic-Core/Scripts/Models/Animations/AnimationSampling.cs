using Unity.Collections;
using UnityEngine;

namespace Gothic.Core.Models.Animations
{
    /// <summary>
    /// Reads the pose of one bone out of a baked AnimationTrack (Positions/Rotations, layout [frame * BoneCount +
    /// bone]) at a fractional frame, interpolating linearly between the two neighbouring samples like the original
    /// engine. Used by AnimationPoseJob (NPCs, Burst, animation worker threads) and MobAnimator (the few rigid nodes of
    /// a used mob, main thread). Static and allocation free, so Burst compiles it into the job.
    /// </summary>
    public static class AnimationSampling
    {
        /// <summary>
        /// The two samples around a fractional frame and the blend between them. Done once per track and frame, the
        /// bones of the track share it.
        /// </summary>
        public static void GetFrames(float frame, int frameCount, out int frame0, out int frame1, out float frameBlend)
        {
            frame0 = Mathf.Clamp((int)frame, 0, frameCount - 1);
            frame1 = Mathf.Min(frame0 + 1, frameCount - 1);
            frameBlend = Mathf.Clamp01(frame - frame0);
        }

        /// <summary>
        /// Local position and rotation of one track bone between two samples (see GetFrames).
        /// Keys are hemisphere-aligned at bake time, so the normalized lerp takes the short way.
        /// </summary>
        public static void SampleBone(NativeArray<Vector3> positions, NativeArray<Quaternion> rotations, int boneCount,
            int frame0, int frame1, float frameBlend, int bone, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.Lerp(
                positions[frame0 * boneCount + bone], positions[frame1 * boneCount + bone], frameBlend);
            rotation = Nlerp(
                rotations[frame0 * boneCount + bone], rotations[frame1 * boneCount + bone], frameBlend);
        }

        /// <summary>
        /// Normalized lerp instead of slerp: between consecutive .man frames the angles are tiny and for
        /// blend ramps both endpoints are the same poses - the angular deviation stays invisible, while a
        /// dot + sqrt replaces the per-bone trigonometry (this runs per bone, per track, per NPC, per frame).
        /// </summary>
        public static Quaternion Nlerp(Quaternion from, Quaternion to, float weight)
        {
            // Take the short way around - blend sources aren't hemisphere-aligned with each other.
            var dot = from.x * to.x + from.y * to.y + from.z * to.z + from.w * to.w;
            var sign = dot < 0f ? -1f : 1f;

            var x = from.x + (to.x * sign - from.x) * weight;
            var y = from.y + (to.y * sign - from.y) * weight;
            var z = from.z + (to.z * sign - from.z) * weight;
            var w = from.w + (to.w * sign - from.w) * weight;

            var scale = 1f / Mathf.Sqrt(x * x + y * y + z * z + w * w);
            return new Quaternion(x * scale, y * scale, z * scale, w * scale);
        }
    }
}
