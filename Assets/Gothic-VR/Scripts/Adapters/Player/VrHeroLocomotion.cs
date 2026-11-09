using System.Collections.Generic;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Legs and torso of the VR hero body like the hero in Gothic: picks the animation and its playback speed from the
    /// player's velocity, the body state, the body's turn speed, crouching and swimming (VrHeroSwimPose) - idle,
    /// walk/run (played at the real speed - no sliding feet), walking backwards, strafing, turning on the spot,
    /// falling, sneaking, swimming and diving. Worker of VRHeroBody (new().Inject(), Bind per built body).
    /// </summary>
    public class VrHeroLocomotion
    {
        [Inject] private readonly AnimationService _animationService;

        private const float _idleSpeed = 0.15f;
        private const float _runSpeed = 1.6f;
        private const float _turnAnimationSpeed = 60f; // degrees per second
        private const float _backwardsShare = 0.5f;
        private const string _idleAnimation = "S_RUN";
        private const string _walkAnimation = "S_WALKL";
        private const string _runAnimation = "S_RUNL";
        private const string _walkBackAnimation = "S_WALKBL";
        private const string _fallAnimation = "S_FALLDN";

        private VRHeroBodyAnimator _animator;
        private float _lastBodyYaw;
        private float _bodyYawSpeed;

        // Water check, speed and animations while swimming/diving.
        private readonly VrHeroSwimPose _swimPose = new();

        // A missing animation (and the animator's overlay) -> its forward loop played back to front, or null - asked
        // every frame while it's missing (e.g. no S_SNEAKBL). Per built body (Bind).
        private readonly Dictionary<(string, string), string> _missingAnimationFallbacks = new();

        /// <summary>
        /// The animator of a newly built body.
        /// </summary>
        public void Bind(VRHeroBodyAnimator animator)
        {
            _animator = animator;
            _missingAnimationFallbacks.Clear();
        }

        /// <summary>
        /// Picks and plays this frame's animation and advances the animator. body: the placed body (its yaw and
        /// facing), scale: its scale (the walk/run loops' real speed).
        /// </summary>
        public void Update(NpcContainer hero, Transform body, IVrRig rig, float scale, bool isCrouching)
        {
            if (_animator == null || !_animator.IsValid)
                return;
            // The speed potion's overlay (VRHeroMovement): the body runs faster too.
            _animator.OverlayOverride = VRHeroMovement.SpeedOverlay;

            var yaw = body.eulerAngles.y;
            if (Time.deltaTime > 0f)
                _bodyYawSpeed = Mathf.Lerp(_bodyYawSpeed, Mathf.DeltaAngle(_lastBodyYaw, yaw) / Time.deltaTime, 0.3f);
            _lastBodyYaw = yaw;

            var velocity = _swimPose.GetVelocity(rig);
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            var speed = horizontal.magnitude;
            var local = body.InverseTransformDirection(horizontal);

            var bodyState = _swimPose.GetBodyState(rig, hero.Props.BodyState);
            var isMoving = speed >= _idleSpeed;
            var isBackwards = local.z < -_backwardsShare * speed;
            var isTurning = Mathf.Abs(_bodyYawSpeed) >= _turnAnimationSpeed;

            var animation = _swimPose.GetAnimation(_animator, bodyState, speed, isMoving, isBackwards, isTurning,
                    _bodyYawSpeed < 0f)
                ?? GetWalkAnimation(rig, velocity, speed, local, isMoving, isBackwards, isTurning, isCrouching);

            if (!_animator.HasAnimation(animation))
            {
                animation = GetMissingAnimationFallback(animation);
                animation ??= speed < _idleSpeed ? _idleAnimation : _walkAnimation;
            }

            // Walk/run loops at the real speed, so the feet don't slide.
            var animationSpeed = _animator.GetMovementSpeed(animation) * scale;
            var playbackSpeed = animationSpeed > 0.1f && speed >= _idleSpeed
                ? Mathf.Clamp(speed / animationSpeed, 0.5f, 2f)
                : 1f;
            // Gothic has no strafe/turn loops, only single steps (t_*) - repeated while moving sideways/turning.
            var isStep = animation.StartsWith("T_", System.StringComparison.OrdinalIgnoreCase);
            _animator.Play(animation, playbackSpeed, isStep);
            _animator.Update(Time.deltaTime);
        }

        /// <summary>
        /// e.g. no sneaking backwards: the forward loop played back to front (..BL -> ..L reversed), null without one.
        /// Remembered per animation and overlay - no string built every frame.
        /// </summary>
        private string GetMissingAnimationFallback(string animation)
        {
            if (animation == null)
                return null;

            var key = (animation, _animator.MdsOverlay);
            if (_missingAnimationFallbacks.TryGetValue(key, out var fallback))
                return fallback;

            var forwardLoop = animation.EndsWith("BL")
                ? animation.Substring(0, animation.Length - 2) + "L"
                : null;
            fallback = forwardLoop != null && _animator.HasAnimation(forwardLoop)
                ? GetReversedAnimationName(forwardLoop)
                : null;
            _missingAnimationFallbacks[key] = fallback;
            return fallback;
        }

        /// <summary>
        /// On land: falling, sneaking, idle/turning on the spot, walking backwards, strafing, walking/running.
        /// </summary>
        private string GetWalkAnimation(IVrRig rig, Vector3 velocity, float speed, Vector3 local, bool isMoving,
            bool isBackwards, bool isTurning, bool isCrouching)
        {
            if (rig != null && !rig.IsGrounded && velocity.y < -1f && !_swimPose.IsInWater &&
                _animator.HasAnimation(_fallAnimation))
                return _fallAnimation;
            if (isCrouching)
                return !isMoving ? "S_SNEAK" : isBackwards ? "S_SNEAKBL" : "S_SNEAKL";
            if (!isMoving)
                return isTurning ? (_bodyYawSpeed < 0f ? "T_RUNTURNL" : "T_RUNTURNR") : _idleAnimation;
            if (isBackwards)
                return _animator.HasAnimation(_walkBackAnimation)
                    ? _walkBackAnimation
                    : GetReversedAnimationName(_walkAnimation);
            if (Mathf.Abs(local.x) > Mathf.Abs(local.z))
                return speed >= _runSpeed
                    ? (local.x < 0f ? "T_RUNSTRAFEL" : "T_RUNSTRAFER")
                    : (local.x < 0f ? "T_WALKSTRAFEL" : "T_WALKSTRAFER");
            return speed >= _runSpeed ? _runAnimation : _walkAnimation;
        }

        /// <summary>
        /// AnimationService caches the reversed names and tracks - no allocation after the first call.
        /// </summary>
        private string GetReversedAnimationName(string animation)
        {
            return _animationService.GetReversedAnimationName(animation, _animator.MdsBase, _animator.MdsOverlay);
        }
    }
}
