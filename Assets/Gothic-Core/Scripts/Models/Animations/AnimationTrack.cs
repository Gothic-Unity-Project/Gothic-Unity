using System.Collections.Generic;
using Gothic.Core.Extensions;
using Unity.Collections;
using UnityEngine;
using ZenKit;

namespace Gothic.Core.Models.Animations
{
    /// <summary>
    /// Immutable, fully managed metadata for one Gothic animation, baked once by AnimationService and shared
    /// across all NPCs using the same model script. Pose data is baked into NativeArrays which are sampled
    /// and blended inside AnimationPoseJob on Unity's animation worker threads.
    /// </summary>
    public class AnimationTrack
    {
        public enum Type
        {
            Animation,
            Alias
        }

        public Type TrackType;

        // MDS metadata. For aliases, these are the values of the alias entry (layer/next/blend times can differ).
        public string Name;
        public string AliasName; // null, if TrackType == Animation
        public int Layer;
        public string NextAni;
        public float BlendIn;
        public float BlendOut;
        public AnimationFlags Flags;
        public AnimationDirection Direction;
        public bool IsLooping;

        // Sitting poses (bench/throne) authored with a flipped Y rotation. Resolved once at bake time so the
        // per-frame pose path and PlayAnimation/StopAnimation needn't string-match the animation name.
        public bool InvertYAxis;

        // Frame metadata used to map Gothic event frame numbers onto baked clip time.
        public int FirstFrame;
        public int FrameCount;
        public float Fps;
        public float FpsSource;
        public float Duration;
        public float FrameTime;

        // Pose samples baked from the .man file (persistent, shared across NPCs). Layout: [frame * BoneCount + bone].
        // The root bone's X/Z positions are pinned to zero - horizontal translation is applied as root motion
        // instead. Its Y channel holds the offset from the skeleton rest height (flying, sitting, jump arcs).
        public NativeArray<Vector3> Positions;
        public NativeArray<Quaternion> Rotations;
        // Track bone index -> mdh node index (mdh nodes match the bone GameObjects/stream handles 1:1).
        public NativeArray<int> BoneToNode;
        public int BoneCount;
        // Frames actually baked - can be lower than FrameCount when the .man holds fewer samples.
        public int BakedFrameCount;
        // False for tracks sharing the buffers of the first-baked track of the same real animation
        // (AniAlias entries) - they must not dispose them.
        public bool OwnsBakedData = true;
        // Cached so per-frame event scans can skip tracks without any events (the vast majority).
        public bool HasEvents;

        // Root motion (calculated from BIP01 samples; see AnimationService.SetClipMovementSpeed).
        public bool IsMoving;
        public Vector3 MovementSpeed;

        // Animation events, cached into managed objects once (no native interop during playback).
        public List<IEventTag> EventTags;
        public List<IEventSoundEffect> SoundEffects;
        public List<IEventParticleEffect> ParticleEffects;
        public List<IEventMorphAnimation> MorphAnimations;

        /// <summary>
        /// Name as requested by the game logic. Aliases are requested (and therefore compared) by their alias name.
        /// </summary>
        public string DisplayName => AliasName ?? Name;

        public bool IsSameAnimation(AnimationTrack otherTrack)
        {
            return DisplayName.EqualsIgnoreCase(otherTrack.DisplayName);
        }

        public bool MatchesName(string animationName)
        {
            // EqualsIgnoreCase null-guards its receiver, so a null argument would spuriously match any track
            // whose Name/AliasName is also null (every non-alias track has a null AliasName).
            if (animationName == null)
                return false;

            return Name.EqualsIgnoreCase(animationName) || AliasName.EqualsIgnoreCase(animationName);
        }

        /// <summary>
        /// Reuse the native buffers of an already baked track of the same real animation instead of baking
        /// a duplicate. Samples are always baked forward; tracks with Direction == Backward are played back
        /// to front at runtime (AnimationTrackInstance.CurrentFrame), so sharing is safe for them too.
        /// </summary>
        public void ShareBakedSamples(AnimationTrack bakedSource)
        {
            OwnsBakedData = false;
            Positions = bakedSource.Positions;
            Rotations = bakedSource.Rotations;
            BoneToNode = bakedSource.BoneToNode;
            BoneCount = bakedSource.BoneCount;
            BakedFrameCount = bakedSource.BakedFrameCount;
        }

        public void Dispose()
        {
            if (!OwnsBakedData)
                return;

            if (Positions.IsCreated)
                Positions.Dispose();
            if (Rotations.IsCreated)
                Rotations.Dispose();
            if (BoneToNode.IsCreated)
                BoneToNode.Dispose();
        }
    }
}
