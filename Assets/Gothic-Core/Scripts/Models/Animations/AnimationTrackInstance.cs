using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using ZenKit;

namespace Gothic.Core.Models.Animations
{
    /// <summary>
    /// Currently playing instance of a Track on a NPC.
    ///
    /// Pose sampling and blending are done inside AnimationPoseJob. This class only owns the Gothic-specific
    /// runtime state: playback clock (with Gothic looping/freeze-on-blend-out semantics), blend weight ramps,
    /// and the event timeline.
    /// </summary>
    public class AnimationTrackInstance
    {
        public readonly AnimationTrack Track;
        public readonly int CreationTime;

        public AnimationState State;
        public float CurrentTime;
        public float Weight;

        public string AnimationName => Track.DisplayName;

        /// <summary>
        /// Playback position in (fractional) sample frames, fed into AnimationPoseJob.
        /// Reversed tracks (ani/aniAlias with direction R, e.g. t_Sit_2_Stand aliasing t_Stand_2_Sit) play
        /// the shared forward-baked samples back to front - the clock itself always runs forward.
        /// </summary>
        public float CurrentFrame => Track.Direction == AnimationDirection.Backward
            ? Track.BakedFrameCount - 1 - CurrentTime / Track.FrameTime
            : CurrentTime / Track.FrameTime;

        private float _blendDuration;

        // Frame position of the previous Update. Events fire when their frame is crossed between two Updates.
        private float _previousFrame = -1f;

        // Reused buffers to avoid per-frame allocations. Null is returned when empty (see GetPending*()).
        // Only allocated when the track has events at all - most tracks (and instances) have none.
        private readonly List<IEventTag> _pendingEventTags;
        private readonly List<IEventSoundEffect> _pendingSoundEffects;
        private readonly List<IEventParticleEffect> _pendingParticleEffects;
        private readonly List<IEventMorphAnimation> _pendingMorphAnimations;


        public AnimationTrackInstance(AnimationTrack track)
        {
            CreationTime = Time.frameCount;
            Track = track;
            CurrentTime = 0f;

            if (track.HasEvents)
            {
                _pendingEventTags = new List<IEventTag>();
                _pendingSoundEffects = new List<IEventSoundEffect>();
                _pendingParticleEffects = new List<IEventParticleEffect>();
                _pendingMorphAnimations = new List<IEventMorphAnimation>();
            }

            // If we have no BlendIn time, the animation needs to play at full weight right from the start.
            if (track.BlendIn <= 0f)
            {
                State = AnimationState.Play;
                Weight = 1f;
            }
            else
            {
                State = AnimationState.BlendIn;
                Weight = 0f;
                _blendDuration = track.BlendIn;
            }
        }

        /// <summary>
        /// Update playback clock, blend weight, and event timeline each frame.
        /// Returns the state change happening this frame (e.g. BlendOut once the last frame is reached) or None.
        /// </summary>
        public AnimationState Update(float deltaTime)
        {
            var stateChange = UpdateWeight(deltaTime);

            if (State == AnimationState.Stop)
            {
                return AnimationState.Stop;
            }

            var wrapped = false;
            if (State != AnimationState.BlendOut)
            {
                CurrentTime += deltaTime;

                if (CurrentTime >= Track.Duration)
                {
                    if (Track.IsLooping)
                    {
                        wrapped = true;
                        CurrentTime %= Track.Duration;
                    }
                    else
                    {
                        // Once the last frame is reached, the animation blends out while freezing at this pose.
                        CurrentTime = Track.Duration;
                        BlendOutTrack(Track.BlendOut);
                        stateChange = AnimationState.BlendOut;
                    }
                }
                // While blending out, CurrentTime stays put - the pose freezes at its last frame (Gothic behavior).
            }

            CollectPendingEvents(wrapped);

            return stateChange;
        }

        private AnimationState UpdateWeight(float deltaTime)
        {
            switch (State)
            {
                case AnimationState.BlendIn:
                    Weight += _blendDuration <= 0f ? 1f : deltaTime / _blendDuration;
                    if (Weight >= 1f)
                    {
                        Weight = 1f;
                        State = AnimationState.Play;
                        return AnimationState.Play;
                    }
                    break;
                case AnimationState.BlendOut:
                    Weight -= _blendDuration <= 0f ? 1f : deltaTime / _blendDuration;
                    if (Weight <= 0f)
                    {
                        Weight = 0f;
                        State = AnimationState.Stop;
                        return AnimationState.Stop;
                    }
                    break;
            }

            return AnimationState.None;
        }

        /// <summary>
        /// About BlendOut times:
        /// a.) the new animation has higher or same Layer, then its BlendIn time is used as our BlendOut time.
        /// b.) there is no new animation, then we use our BlendOut time.
        /// </summary>
        public void BlendOutTrack(float blendOutTime)
        {
            if (State is AnimationState.BlendOut or AnimationState.Stop)
            {
                return;
            }

            State = AnimationState.BlendOut;
            _blendDuration = blendOutTime;
        }

        /// <summary>
        /// Collect all events whose (normalized) frame was crossed since the previous Update.
        /// Handles loop wrap-around so that events close to the last frame aren't dropped (the old
        /// implementation reset its cursors on wrap and silently skipped them).
        /// </summary>
        private void CollectPendingEvents(bool wrapped)
        {
            // No events on this track - skip the whole timeline bookkeeping (the common case).
            if (!Track.HasEvents)
            {
                return;
            }

            _pendingEventTags.Clear();
            _pendingSoundEffects.Clear();
            _pendingParticleEffects.Clear();
            _pendingMorphAnimations.Clear();

            var currentFrame = CurrentTime / Track.FrameTime;

            foreach (var eventTag in Track.EventTags)
            {
                // Event frame=0 has special handling: use as frame 0 without normalization.
                var frame = eventTag.Frame == 0 ? 0f : ClampFrame(eventTag.Frame);
                if (IsFrameCrossed(frame, currentFrame, wrapped))
                    _pendingEventTags.Add(eventTag);
            }

            foreach (var sfx in Track.SoundEffects)
            {
                if (IsFrameCrossed(ClampFrame(sfx.Frame), currentFrame, wrapped))
                    _pendingSoundEffects.Add(sfx);
            }

            foreach (var pfx in Track.ParticleEffects)
            {
                if (IsFrameCrossed(ClampFrame(pfx.Frame), currentFrame, wrapped))
                    _pendingParticleEffects.Add(pfx);
            }

            foreach (var morph in Track.MorphAnimations)
            {
                if (IsFrameCrossed(ClampFrame(morph.Frame), currentFrame, wrapped))
                    _pendingMorphAnimations.Add(morph);
            }

            _previousFrame = currentFrame;
        }

        private bool IsFrameCrossed(float eventFrame, float currentFrame, bool wrapped)
        {
            if (wrapped)
            {
                // We passed the end of the animation and restarted: fire tail events and early events alike.
                return eventFrame > _previousFrame || eventFrame <= currentFrame;
            }

            return eventFrame > _previousFrame && eventFrame <= currentFrame;
        }

        /// <summary>
        /// This method solves multiple circumstances:
        /// (1). Gothic animations won't always start from frame 0. e.g. t_Potion_Random_1 expects to work from frame 45+.
        ///      --> This might be, as the animations are "behind" another and could be one single animation in Gothic.
        ///      --> But in Gothic, we create every transition animation separately and therefore normalize to start from frame 0.
        /// (2). G1 animation key frames are optimized and not always aligned with 25fps (e.g. t_Potion_* leverages 10 frames only).
        ///      But the animation event frame numbers are matching 25fps.
        ///      --> We therefore calculate the ratio between the fpsSource (G1=25fps) and the actual fps (e.g. 10fps).
        /// (3). Some animation events seem to be executed before or after the actual animation.
        ///      --> We take care by checking its boundaries.
        /// </summary>
        private float ClampFrame(int expectedFrame)
        {
            // (2). calculate ratio between FpsSource and the animation's Fps.
            var animationRatio = Track.Fps / Track.FpsSource;

            // (1). Norm to start frame of 0. (2). Norm to fpsSource (==25 in G1).
            var frame = (float)Math.Round((expectedFrame - Track.FirstFrame) * animationRatio);

            // Reversed tracks play their samples back to front - the event timeline mirrors with them.
            if (Track.Direction == AnimationDirection.Backward)
                frame = Track.FrameCount - 1 - frame;

            // (3). check for misaligned animation frame boundaries (if any).
            return Mathf.Clamp(frame, 0, Track.FrameCount - 1);
        }

        [CanBeNull]
        public List<IEventTag> GetPendingEventTags()
        {
            return _pendingEventTags == null || _pendingEventTags.Count == 0 ? null : _pendingEventTags;
        }

        [CanBeNull]
        public List<IEventSoundEffect> GetPendingSoundEffects()
        {
            return _pendingSoundEffects == null || _pendingSoundEffects.Count == 0 ? null : _pendingSoundEffects;
        }

        [CanBeNull]
        public List<IEventParticleEffect> GetPendingParticleEffects()
        {
            return _pendingParticleEffects == null || _pendingParticleEffects.Count == 0 ? null : _pendingParticleEffects;
        }

        [CanBeNull]
        public List<IEventMorphAnimation> GetPendingMorphAnimations()
        {
            return _pendingMorphAnimations == null || _pendingMorphAnimations.Count == 0 ? null : _pendingMorphAnimations;
        }
    }
}
