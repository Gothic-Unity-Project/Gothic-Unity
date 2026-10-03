#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.Core.Models.Animations;
using Gothic.Core.Services.Npc;
using UnityEngine;
using ZenKit;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// V2 of the VR hero body: legs and torso play the hero's Gothic animations (idle, walk, run, backwards, strafe,
    /// turn, fall) - sampled from the baked tracks of AnimationService on the main thread. No AnimationSystem: that one
    /// belongs to an NPC (root motion, events, physics capsule). The arms (clavicles down) are left out, they belong to
    /// the IK; the head is hidden anyway. Simple crossfade: the newest track blends in over the older ones.
    /// </summary>
    public class VRHeroBodyAnimator
    {
        private const float _blendTime = 0.2f;
        private static readonly string[] _armBones = { "CLAVICLE", "UPPERARM", "FOREARM", "HAND", "FINGER" };

        private readonly AnimationService _animationService;
        private readonly string _mdsBase;
        private readonly string _mdsOverlay;
        // mdh node index -> bone of the body (null = not animated: arms, missing bones).
        private readonly Transform[] _nodeBones;
        private readonly int _rootNodeIndex;
        private readonly List<Layer> _layers = new();
        private Layer _overlay;
        private float _overlayTargetWeight;
        private const float _overlayBlendTime = 1.5f;

        public bool IsValid => _nodeBones != null;
        public string MdsBase => _mdsBase;
        public string MdsOverlay => OverlayOverride ?? _mdsOverlay;
        // A timed overlay of the hero (speed potion: HUMANS_SPRINT.MDS) - its animations come first while it's set.
        public string OverlayOverride;

        private class Layer
        {
            public AnimationTrack Track;
            public float Time;
            public float Speed;
            public float Weight;
            // One-shot steps (t_RunStrafeL, t_RunTurnL) repeated while the movement lasts.
            public bool IsForcedLoop;
        }


        public VRHeroBodyAnimator(AnimationService animationService, string mdsBase, string mdsOverlay,
            Dictionary<string, Transform> bones)
        {
            _animationService = animationService;
            _mdsBase = mdsBase;
            _mdsOverlay = mdsOverlay;

            var skeleton = animationService.GetSkeleton(mdsBase);
            if (skeleton == null)
                return;

            _rootNodeIndex = skeleton.RootNodeIndex;
            _nodeBones = new Transform[skeleton.NodeCount];
            for (var node = 0; node < skeleton.NodeCount; node++)
            {
                var path = skeleton.Paths[node];
                var boneName = path.Substring(path.LastIndexOf('/') + 1).ToUpperInvariant();
                if (IsArmBone(boneName) || !bones.TryGetValue(boneName, out var bone))
                    continue;
                _nodeBones[node] = bone;
            }
        }

        private static bool IsArmBone(string boneName)
        {
            foreach (var arm in _armBones)
            {
                if (boneName.Contains(arm))
                    return true;
            }
            return false;
        }

        public bool HasAnimation(string animationName) => GetTrack(animationName) != null;

        private AnimationTrack GetTrack(string animationName) =>
            animationName == null ? null : _animationService.GetTrack(animationName, _mdsBase, MdsOverlay);

        /// <summary>
        /// Crossfades to the animation (if not already the current one). Speed scales the playback, e.g. to the real
        /// walking speed so the feet don't slide.
        /// </summary>
        public bool Play(string animationName, float speed = 1f, bool isForcedLoop = false)
        {
            var track = GetTrack(animationName);
            if (track == null)
                return false;

            if (_layers.Count > 0 && _layers[^1].Track == track)
            {
                _layers[^1].Speed = speed;
                _layers[^1].IsForcedLoop = isForcedLoop;
                return true;
            }

            _layers.Add(new Layer
            {
                Track = track, Speed = speed, Weight = _layers.Count == 0 ? 1f : 0f, IsForcedLoop = isForcedLoop
            });
            return true;
        }

        /// <summary>
        /// Movement speed of the animation at model scale 1 (root motion of walk/run loops), 0 for poses.
        /// </summary>
        public float GetMovementSpeed(string animationName)
        {
            var track = GetTrack(animationName);
            return track != null && track.IsMoving ? track.MovementSpeed.magnitude : 0f;
        }

        public void Update(float deltaTime)
        {
            if (!IsValid || _layers.Count == 0)
                return;

            var newest = _layers[^1];
            newest.Weight = Mathf.MoveTowards(newest.Weight, 1f, deltaTime / _blendTime);
            for (var i = _layers.Count - 2; i >= 0; i--)
            {
                // Blending out with the newest one blending in - gone once it's fully covered.
                if (newest.Weight >= 1f)
                    _layers.RemoveAt(i);
            }

            foreach (var layer in _layers)
            {
                var track = layer.Track;
                layer.Time += deltaTime * layer.Speed;
                layer.Time = track.IsLooping || layer.IsForcedLoop
                    ? Mathf.Repeat(layer.Time, track.Duration)
                    : Mathf.Min(layer.Time, track.Duration);
            }

            for (var i = 0; i < _layers.Count; i++)
                ApplyLayer(_layers[i], i == 0 ? 1f : _layers[i].Weight);

            UpdateOverlay(deltaTime);
        }

        /// <summary>
        /// A second animation blended over the main one at a slowly changing weight (swimming: the crawl over the
        /// floating pose - the legs rise towards the surface the faster you swim, instead of snapping behind you).
        /// </summary>
        public void SetOverlay(string animationName, float weight)
        {
            var track = GetTrack(animationName);
            if (track != null && _overlay?.Track != track)
            {
                if (_overlay == null || _overlay.Weight <= 0f)
                    _overlay = new Layer { Track = track, Speed = 1f };
                else
                    _overlay.Track = track;
            }
            _overlayTargetWeight = track != null ? Mathf.Clamp01(weight) : 0f;
        }

        private void UpdateOverlay(float deltaTime)
        {
            if (_overlay == null)
                return;

            _overlay.Weight = Mathf.MoveTowards(_overlay.Weight, _overlayTargetWeight, deltaTime / _overlayBlendTime);
            if (_overlay.Weight <= 0f && _overlayTargetWeight <= 0f)
            {
                _overlay = null;
                return;
            }

            _overlay.Time = Mathf.Repeat(_overlay.Time + deltaTime, _overlay.Track.Duration);
            ApplyLayer(_overlay, _overlay.Weight);
        }

        /// <summary>
        /// Pose of one track blended over the current pose (like AnimationPoseJob: only the bones it drives).
        /// </summary>
        private void ApplyLayer(Layer layer, float weight)
        {
            var track = layer.Track;
            if (track.BakedFrameCount <= 0 || track.BoneCount <= 0 || !track.Rotations.IsCreated)
                return;

            var frame = layer.Time / track.FrameTime;
            if (track.Direction == AnimationDirection.Backward)
                frame = track.BakedFrameCount - 1 - frame;
            frame = Mathf.Clamp(frame, 0f, track.BakedFrameCount - 1);
            var frameA = Mathf.FloorToInt(frame);
            var frameB = track.IsLooping
                ? (frameA + 1) % track.BakedFrameCount
                : Mathf.Min(frameA + 1, track.BakedFrameCount - 1);
            var t = frame - frameA;

            for (var bone = 0; bone < track.BoneCount; bone++)
            {
                var node = track.BoneToNode[bone];
                if (node < 0 || node >= _nodeBones.Length)
                    continue;
                var boneTransform = _nodeBones[node];
                if (boneTransform == null)
                    continue;

                var rotation = Quaternion.Slerp(track.Rotations[frameA * track.BoneCount + bone],
                    track.Rotations[frameB * track.BoneCount + bone], t);
                boneTransform.localRotation = weight >= 1f
                    ? rotation
                    : Quaternion.Slerp(boneTransform.localRotation, rotation, weight);

                // The root's height is taken over by the body placement (neck under the goggles).
                if (node == _rootNodeIndex)
                    continue;

                var position = Vector3.Lerp(track.Positions[frameA * track.BoneCount + bone],
                    track.Positions[frameB * track.BoneCount + bone], t);
                boneTransform.localPosition = weight >= 1f
                    ? position
                    : Vector3.Lerp(boneTransform.localPosition, position, weight);
            }
        }
    }
}
#endif
