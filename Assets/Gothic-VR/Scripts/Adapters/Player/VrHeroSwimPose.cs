using Gothic.Core.Const;
using Gothic.Core.Models.Vm;
using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The hero body in the water: its own water check from the feet (swimming even while the hero's swim state
    /// flickers at the surface), the player's speed while swimming (no controller velocity then) and the swim/dive
    /// animations - floating upright (S_SWIM), the crawl (S_SWIMF) blended in with the speed, swimming backwards,
    /// turning while drifting, diving. VRHeroBody only asks for the swim animation and plays the walking set without
    /// one. Worker of VRHeroBody (new()).
    /// </summary>
    public class VrHeroSwimPose
    {
        // The body looks at the water itself - the swim state flickered while
        // bobbing at the surface (chest -> knee deep for a moment = walking, not grounded = the fall animation).
        private const float _swimWaterDepth = 1.1f;
        private const float _waterSurfaceRayLength = 3f;
        private const float _bottomRayStart = 0.1f;
        private const float _floatAboveBottom = 0.15f;

        // The body's own speed from how far the player moved (teleport jumps ignored).
        private Vector3 _lastPlayerPosition;
        private Vector3 _movedVelocity;
        private const float _movedSmoothing = 0.2f;
        private const float _maxMovedSpeed = 15f;

        private const float _swimCrawlFullSpeed = 0.8f;
        private const float _swimCrawlMaxWeight = 0.8f;
        private const string _swimCrawlAnimation = "S_SWIMF";

        /// <summary>
        /// Any water above the feet (last GetBodyState) - no fall animation then.
        /// </summary>
        public bool IsInWater { get; private set; }

        /// <summary>
        /// The player's velocity: the controller's, or the movement between frames if that's faster - swimming moves
        /// the player without a controller velocity (~0: no crawl, no strokes while swimming with the stick/WASD).
        /// </summary>
        public Vector3 GetVelocity(IVrRig rig)
        {
            if (rig == null)
                return Vector3.zero;

            var velocity = rig.CharacterController.velocity;
            if (Time.deltaTime <= 0f)
                return velocity;

            var position = rig.Root.position;
            var moved = (position - _lastPlayerPosition) / Time.deltaTime;
            _lastPlayerPosition = position;
            // A teleport isn't a speed.
            if (moved.magnitude < _maxMovedSpeed)
                _movedVelocity = Vector3.Lerp(_movedVelocity, moved, _movedSmoothing);
            if (new Vector2(_movedVelocity.x, _movedVelocity.z).sqrMagnitude >
                new Vector2(velocity.x, velocity.z).sqrMagnitude)
                velocity = _movedVelocity;
            return velocity;
        }

        /// <summary>
        /// The hero's body state, but deep water is swimming even while the state flickers. Sets IsInWater.
        /// </summary>
        public VmGothicEnums.BodyState GetBodyState(IVrRig rig, VmGothicEnums.BodyState bodyState)
        {
            IsInWater = false;
            if (rig == null)
                return bodyState;

            // Own rays from the feet: the water service's ray from the bottom up hit the player's own capsule while
            // floating above the bottom (no water found - the body stood upright in deep water).
            var feet = rig.Root.position;
            if (!Physics.Raycast(feet, Vector3.up, out var surfaceHit, _waterSurfaceRayLength,
                    1 << Constants.WaterLayer, QueryTriggerInteraction.Collide))
                return bodyState;

            IsInWater = true;
            var bottom = Physics.Raycast(feet + Vector3.up * _bottomRayStart, Vector3.down, out var bottomHit,
                _swimWaterDepth, 1 << Constants.DefaultLayer, QueryTriggerInteraction.Ignore)
                ? bottomHit.distance - _bottomRayStart
                : _swimWaterDepth;
            var isFloating = bottom > _floatAboveBottom;
            if (isFloating && surfaceHit.distance + bottom >= _swimWaterDepth &&
                bodyState != VmGothicEnums.BodyState.BsDive)
                return VmGothicEnums.BodyState.BsSwim;
            return bodyState;
        }

        /// <summary>
        /// The swim/dive animation for the body state, null if the hero isn't swimming or diving. Always sets the
        /// crawl overlay (0 out of the water).
        /// </summary>
        public string GetAnimation(VRHeroBodyAnimator animator, VmGothicEnums.BodyState bodyState, float speed,
            bool isMoving, bool isBackwards, bool isTurning, bool isTurningLeft)
        {
            // Floating upright (S_SWIM), the crawl (S_SWIMF) only blended in with the speed - slowly, so the legs
            // come up towards the surface instead of jumping behind the camera.
            var swimCrawl = bodyState == VmGothicEnums.BodyState.BsSwim && isMoving && !isBackwards
                ? Mathf.Clamp01(speed / _swimCrawlFullSpeed) * _swimCrawlMaxWeight
                : 0f;
            animator.SetOverlay(_swimCrawlAnimation, swimCrawl);

            // Drifting (not moving) while turning plays the swim/dive turn.
            var isDriftTurning = !isMoving && isTurning;
            var turnSide = isTurningLeft ? "L" : "R";

            if (bodyState == VmGothicEnums.BodyState.BsSwim)
                return isDriftTurning && animator.HasAnimation("T_SWIMTURN" + turnSide) ? "T_SWIMTURN" + turnSide
                    : isMoving && isBackwards ? "S_SWIMB" : "S_SWIM";
            if (bodyState == VmGothicEnums.BodyState.BsDive)
                return isDriftTurning && animator.HasAnimation("T_DIVETURN" + turnSide) ? "T_DIVETURN" + turnSide
                    : isMoving ? "S_DIVEF" : "S_DIVE";
            return null;
        }
    }
}
