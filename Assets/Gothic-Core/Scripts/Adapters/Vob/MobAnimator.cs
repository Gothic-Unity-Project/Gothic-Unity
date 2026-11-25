using System.Collections.Generic;
using Gothic.Core.Adapters.Animations;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Animations;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using AnimationState = Gothic.Core.Models.Animations.AnimationState;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// Plays a mob's own animations while it is used (MobAnimationService decides what): a transition, then the
    /// state's loop, with the tracks' sounds and particles. Created on first use by MobAnimatorFactory.
    ///
    /// A light player instead of AnimationSystem: a mob's nodes are rigid (an anvil's hammer spot, a grindstone's
    /// wheel), a few per mob, so they're sampled on the CPU (AnimationSampling) and written to the node transforms -
    /// no PlayableGraph, no blending. Like NPC tracks, the baked root node has X/Z pinned to zero, which matches mobs:
    /// the mesh builder rests their root node at zero.
    ///
    /// Update runs only while a transition or loop plays, else the component is disabled. A mob culled with its VOB
    /// pauses and goes on when it's visible again.
    /// </summary>
    public class MobAnimator : MonoBehaviour, IMobAnimator
    {
        [Inject] private readonly MobAnimationService _mobAnimationService;

        private VobContainer _mob;
        // The mob's node transforms, index == mdh node index (AnimationTrack.BoneToNode). Null if not found.
        private Transform[] _nodes;
        private AnimationEffectPlayer _effectPlayer;
        private readonly List<NpcContainer> _users = new();

        private AnimationTrackInstance _current;
        private bool _isCurrentLoop;
        private float _previousTime;
        // Played once the current transition ended (null: the mob rests in its last pose).
        private AnimationTrack _nextLoop;
        // Stopping: after the current transition (and the transition back, if any) the mob rests.
        private bool _isStopPending;
        private AnimationTrack _pendingBack;

        public int State { get; private set; }

        public int UserCount => _users.Count;


        /// <summary>
        /// Called once by MobAnimatorFactory: maps the skeleton's nodes to the mob's node GameObjects and creates the
        /// sound source for its *eventSFX (3D, linear fall-off to the event's range like an NPC's sound).
        /// </summary>
        public void Init(VobContainer mob, AnimationSkeleton skeleton)
        {
            _mob = mob;
            MapNodes(skeleton);

            var sound = gameObject.AddComponent<AudioSource>();
            sound.playOnAwake = false;
            sound.spatialBlend = 1f;
            sound.rolloffMode = AudioRolloffMode.Linear;
            sound.minDistance = 0f;
            _effectPlayer = new AnimationEffectPlayer(transform, transform, sound, isReusingPfx: true).Inject();

            enabled = false;
        }

        /// <summary>
        /// The node GameObjects carry the names of the model's nodes (Gothic-defined, architecture.md rule C): the root
        /// node is searched once below the mob, the others by their path below the root's parent.
        /// </summary>
        private void MapNodes(AnimationSkeleton skeleton)
        {
            _nodes = new Transform[skeleton.NodeCount];
            if (skeleton.RootNodeIndex < 0)
                return;

            var rootName = skeleton.Paths[skeleton.RootNodeIndex];
            var root = transform.FindChildRecursively(rootName);
            if (root == null)
            {
                Logger.LogWarning($"[MobAnimation] {name}: node {rootName} not found - the mob won't move.",
                    LogCat.Vob);
                return;
            }

            var missingCount = 0;
            for (var node = 0; node < skeleton.NodeCount; node++)
            {
                _nodes[node] = root.parent.Find(skeleton.Paths[node]);
                if (_nodes[node] == null)
                    missingCount++;
            }

            if (missingCount > 0)
                Logger.LogWarning($"[MobAnimation] {name}: {missingCount} of {skeleton.NodeCount} nodes not found.",
                    LogCat.Vob);
        }

        public bool HasUser(NpcContainer user)
        {
            return _users.Contains(user);
        }

        public void AddUser(NpcContainer user)
        {
            if (user != null && !_users.Contains(user))
                _users.Add(user);
        }

        public void RemoveUser(NpcContainer user)
        {
            _users.Remove(user);
        }

        public void Play(int state, AnimationTrack transition, AnimationTrack loop)
        {
            State = state;
            _nextLoop = loop;
            _isStopPending = false;
            _pendingBack = null;

            if (transition != null)
                StartTrack(transition, isLoop: false);
            else if (loop != null)
                StartTrack(loop, isLoop: true);
            else
                Rest();
        }

        public void PlayBackAndStop(AnimationTrack backTransition)
        {
            State = Mathf.Max(0, State - 1);
            _nextLoop = null;
            _isStopPending = true;

            // A transition still runs (T_S0_2_S1 of a user who left at once): the way back starts after it.
            if (_current != null && !_isCurrentLoop)
            {
                _pendingBack = backTransition;
                return;
            }

            if (backTransition != null)
                StartTrack(backTransition, isLoop: false);
            else
                Rest();
        }

        private void StartTrack(AnimationTrack track, bool isLoop)
        {
            _current = new AnimationTrackInstance(track);
            _isCurrentLoop = isLoop;
            _previousTime = 0f;
            enabled = true;
        }

        /// <summary>
        /// Nothing plays any more: the nodes keep their last pose, the particles fade out, Update is off.
        /// </summary>
        private void Rest()
        {
            _current = null;
            _isCurrentLoop = false;
            _nextLoop = null;
            _isStopPending = false;
            _pendingBack = null;
            _effectPlayer?.StopAllPfx();
            enabled = false;
        }

        private void Update()
        {
            if (_current == null)
            {
                enabled = false;
                return;
            }

            var stateChange = _current.Update(Time.deltaTime);
            ApplyPose(_current);

            if (_current.Track.HasEvents)
            {
                _effectPlayer.ApplySfxEvents(_current);
                _effectPlayer.ApplyPfxEvents(_current);
            }

            // A track without a loop of its own reached its last frame (it starts blending out).
            if (stateChange is AnimationState.BlendOut or AnimationState.Stop)
            {
                if (_isCurrentLoop)
                    OnLoopWrapped(restartTrack: true);
                else
                    OnTransitionEnded();
                return;
            }

            if (_isCurrentLoop && _current.CurrentTime < _previousTime)
            {
                OnLoopWrapped(restartTrack: false);
                return;
            }

            _previousTime = _current.CurrentTime;
        }

        private void OnTransitionEnded()
        {
            if (_pendingBack != null)
            {
                var back = _pendingBack;
                _pendingBack = null;
                StartTrack(back, isLoop: false);
                return;
            }

            if (_isStopPending || _nextLoop == null)
            {
                Rest();
                return;
            }

            StartTrack(_nextLoop, isLoop: true);
        }

        /// <summary>
        /// The safety net, once per loop round (not per frame): a user who left without the mob being released (a
        /// path that doesn't end the usage, a culled or killed NPC) stops the animation.
        /// </summary>
        private void OnLoopWrapped(bool restartTrack)
        {
            if (restartTrack)
                StartTrack(_current.Track, isLoop: true);
            else
                _previousTime = _current.CurrentTime;

            for (var i = _users.Count - 1; i >= 0; i--)
            {
                // Stop() removes exactly this user, the ones below stay at their index.
                var user = _users[i];
                if (_mobAnimationService.IsStillUsing(_mob, user))
                    continue;

                Logger.Log($"[MobAnimation] {name}: a user left without releasing it - stopping.", LogCat.Vob);
                _mobAnimationService.Stop(_mob, user);
            }

            if (_users.Count == 0 && _isCurrentLoop)
                Rest();
        }

        private void ApplyPose(AnimationTrackInstance instance)
        {
            var track = instance.Track;
            if (track.BoneCount == 0 || track.BakedFrameCount == 0)
                return;

            AnimationSampling.GetFrames(instance.CurrentFrame, track.BakedFrameCount, out var frame0, out var frame1,
                out var frameBlend);

            for (var bone = 0; bone < track.BoneCount; bone++)
            {
                var nodeIndex = track.BoneToNode[bone];
                if (nodeIndex < 0 || nodeIndex >= _nodes.Length)
                    continue;

                var node = _nodes[nodeIndex];
                if (node == null)
                    continue;

                AnimationSampling.SampleBone(track.Positions, track.Rotations, track.BoneCount, frame0, frame1,
                    frameBlend, bone, out var position, out var rotation);
                node.SetLocalPositionAndRotation(position, rotation);
            }
        }
    }
}
