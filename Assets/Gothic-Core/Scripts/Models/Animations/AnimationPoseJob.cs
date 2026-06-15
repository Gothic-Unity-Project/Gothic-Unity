using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace Gothic.Core.Models.Animations
{
    /// <summary>
    /// Per-slot parameters of one active track inside AnimationPoseJob.
    /// </summary>
    public struct AnimationPoseJobTrack
    {
        public float Frame; // Playback position in (fractional) sample frames.
        public int FrameCount; // Baked sample frames of the track.
        public int BoneCount;
        public float Weight; // Gothic blend weight (0..1).
    }

    /// <summary>
    /// Samples and blends all active Gothic animation tracks of one NPC inside Unity's animation pass
    /// (worker threads, evaluated between Update and LateUpdate).
    ///
    /// Why a job instead of baked AnimationClips: AnimationClip.SetCurve only works on legacy clips outside
    /// the Editor ("Can't use AnimationClip::SetCurve at Runtime on non Legacy AnimationClips"), and legacy
    /// clips can't be used with Playables. The job reads the .man samples (NativeArrays baked once per track
    /// by AnimationService) and writes the final pose itself:
    /// 1. Every bone starts at the pose of the previous evaluation (PosePositions/PoseRotations). Bones no
    ///    active track drives keep their last pose, like in the original engine - e.g. s_Bench_S1 doesn't
    ///    drive BIP01 and relies on the height the t_Bench_S0_2_S1 transition left it at.
    /// 2. Tracks are applied in (Gothic layer ASC, creation ASC) order; each overrides exactly the bones it
    ///    drives, blended over the layers below by its current weight - Gothic's layer/subset semantics.
    /// 3. The blended result is read back into the pose buffers for the next evaluation.
    ///
    /// Slots are fixed because jobs can't hold a variable amount of NativeArrays. Gothic rarely plays more
    /// than 3 tracks at once on one NPC; overflow is logged by AnimationSystem.
    /// </summary>
    [BurstCompile]
    public struct AnimationPoseJob : IAnimationJob
    {
        public const int MaxTracks = 8;

        [ReadOnly] public NativeArray<TransformStreamHandle> Handles; // One per mdh node.

        // Persistent pose of the previous evaluation, owned per NPC by AnimationSystem (initialized with the
        // skeleton rest pose). Read as the blend base, written back with the final result of this evaluation.
        public NativeArray<Vector3> PosePositions;
        public NativeArray<Quaternion> PoseRotations;

        public int TrackCount;

        public AnimationPoseJobTrack Track0;
        public AnimationPoseJobTrack Track1;
        public AnimationPoseJobTrack Track2;
        public AnimationPoseJobTrack Track3;
        public AnimationPoseJobTrack Track4;
        public AnimationPoseJobTrack Track5;
        public AnimationPoseJobTrack Track6;
        public AnimationPoseJobTrack Track7;

        [ReadOnly] public NativeArray<Vector3> Positions0;
        [ReadOnly] public NativeArray<Vector3> Positions1;
        [ReadOnly] public NativeArray<Vector3> Positions2;
        [ReadOnly] public NativeArray<Vector3> Positions3;
        [ReadOnly] public NativeArray<Vector3> Positions4;
        [ReadOnly] public NativeArray<Vector3> Positions5;
        [ReadOnly] public NativeArray<Vector3> Positions6;
        [ReadOnly] public NativeArray<Vector3> Positions7;

        [ReadOnly] public NativeArray<Quaternion> Rotations0;
        [ReadOnly] public NativeArray<Quaternion> Rotations1;
        [ReadOnly] public NativeArray<Quaternion> Rotations2;
        [ReadOnly] public NativeArray<Quaternion> Rotations3;
        [ReadOnly] public NativeArray<Quaternion> Rotations4;
        [ReadOnly] public NativeArray<Quaternion> Rotations5;
        [ReadOnly] public NativeArray<Quaternion> Rotations6;
        [ReadOnly] public NativeArray<Quaternion> Rotations7;

        [ReadOnly] public NativeArray<int> BoneToNode0;
        [ReadOnly] public NativeArray<int> BoneToNode1;
        [ReadOnly] public NativeArray<int> BoneToNode2;
        [ReadOnly] public NativeArray<int> BoneToNode3;
        [ReadOnly] public NativeArray<int> BoneToNode4;
        [ReadOnly] public NativeArray<int> BoneToNode5;
        [ReadOnly] public NativeArray<int> BoneToNode6;
        [ReadOnly] public NativeArray<int> BoneToNode7;

        /// <summary>
        /// Assign one track slot. Unused slots must still hold created arrays - AnimationSystem fills them
        /// with the rest pose arrays and an empty bone map.
        /// </summary>
        public void SetTrack(int slot, AnimationPoseJobTrack track, NativeArray<Vector3> positions,
            NativeArray<Quaternion> rotations, NativeArray<int> boneToNode)
        {
            switch (slot)
            {
                case 0: Track0 = track; Positions0 = positions; Rotations0 = rotations; BoneToNode0 = boneToNode; break;
                case 1: Track1 = track; Positions1 = positions; Rotations1 = rotations; BoneToNode1 = boneToNode; break;
                case 2: Track2 = track; Positions2 = positions; Rotations2 = rotations; BoneToNode2 = boneToNode; break;
                case 3: Track3 = track; Positions3 = positions; Rotations3 = rotations; BoneToNode3 = boneToNode; break;
                case 4: Track4 = track; Positions4 = positions; Rotations4 = rotations; BoneToNode4 = boneToNode; break;
                case 5: Track5 = track; Positions5 = positions; Rotations5 = rotations; BoneToNode5 = boneToNode; break;
                case 6: Track6 = track; Positions6 = positions; Rotations6 = rotations; BoneToNode6 = boneToNode; break;
                case 7: Track7 = track; Positions7 = positions; Rotations7 = rotations; BoneToNode7 = boneToNode; break;
            }
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
            // Root motion is applied on the NPC root by AnimationSystem.ApplyFinalMovement (Gothic rubber band).
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            // Start from the previous evaluation's pose: bones no active track drives keep their last pose
            // (Gothic behavior), and blend weights < 1 fade from the currently visible pose.
            for (var node = 0; node < Handles.Length; node++)
            {
                var handle = Handles[node];
                if (!handle.IsValid(stream))
                    continue;

                handle.SetLocalPosition(stream, PosePositions[node]);
                handle.SetLocalRotation(stream, PoseRotations[node]);
            }

            if (TrackCount > 0) ApplyTrack(stream, Track0, Positions0, Rotations0, BoneToNode0);
            if (TrackCount > 1) ApplyTrack(stream, Track1, Positions1, Rotations1, BoneToNode1);
            if (TrackCount > 2) ApplyTrack(stream, Track2, Positions2, Rotations2, BoneToNode2);
            if (TrackCount > 3) ApplyTrack(stream, Track3, Positions3, Rotations3, BoneToNode3);
            if (TrackCount > 4) ApplyTrack(stream, Track4, Positions4, Rotations4, BoneToNode4);
            if (TrackCount > 5) ApplyTrack(stream, Track5, Positions5, Rotations5, BoneToNode5);
            if (TrackCount > 6) ApplyTrack(stream, Track6, Positions6, Rotations6, BoneToNode6);
            if (TrackCount > 7) ApplyTrack(stream, Track7, Positions7, Rotations7, BoneToNode7);

            // Persist the final pose as the base of the next evaluation.
            for (var node = 0; node < Handles.Length; node++)
            {
                var handle = Handles[node];
                if (!handle.IsValid(stream))
                    continue;

                PosePositions[node] = handle.GetLocalPosition(stream);
                PoseRotations[node] = handle.GetLocalRotation(stream);
            }
        }

        private void ApplyTrack(AnimationStream stream, AnimationPoseJobTrack track,
            NativeArray<Vector3> positions, NativeArray<Quaternion> rotations, NativeArray<int> boneToNode)
        {
            if (track.Weight <= 0f || track.BoneCount == 0)
                return;

            var frame0 = Mathf.Clamp((int)track.Frame, 0, track.FrameCount - 1);
            var frame1 = Mathf.Min(frame0 + 1, track.FrameCount - 1);
            var frameBlend = Mathf.Clamp01(track.Frame - frame0);

            for (var bone = 0; bone < track.BoneCount; bone++)
            {
                var handle = Handles[boneToNode[bone]];
                if (!handle.IsValid(stream))
                    continue;

                // Keys are hemisphere-aligned at bake time, so the normalized lerp takes the short way.
                var position = Vector3.Lerp(
                    positions[frame0 * track.BoneCount + bone], positions[frame1 * track.BoneCount + bone], frameBlend);
                var rotation = Nlerp(
                    rotations[frame0 * track.BoneCount + bone], rotations[frame1 * track.BoneCount + bone], frameBlend);

                if (track.Weight < 1f)
                {
                    // Blend over whatever lower layers (or the rest pose) wrote before us.
                    position = Vector3.Lerp(handle.GetLocalPosition(stream), position, track.Weight);
                    rotation = Nlerp(handle.GetLocalRotation(stream), rotation, track.Weight);
                }

                handle.SetLocalPosition(stream, position);
                handle.SetLocalRotation(stream, rotation);
            }
        }

        /// <summary>
        /// Normalized lerp instead of slerp: between consecutive .man frames the angles are tiny and for
        /// blend ramps both endpoints are the same poses - the angular deviation stays invisible, while a
        /// dot + sqrt replaces the per-bone trigonometry (this runs per bone, per track, per NPC, per frame).
        /// </summary>
        private static Quaternion Nlerp(Quaternion from, Quaternion to, float weight)
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
