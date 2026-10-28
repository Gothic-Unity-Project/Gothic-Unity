using Gothic.VR.Interaction.Ports;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// JumpProvider whose height Gothic decides per jump (IVrLedgeHandler, ledge climbing). Adds what XRI lacks:
    /// - The handler is asked once per press, before XRI jumps. It may take the press for something else (a comfort
    ///   climb): XRI doesn't see the press until the button is released.
    /// - jumpHeight is set once at take-off: XRI reads it on every frame of the jump.
    /// - jumpHeight isn't metres (force approximation with gravity on): GetJumpHeightForRise() inverts XRI's force
    ///   curve for a wanted rise.
    /// - CancelJump(): a hand grabbing a ledge or ladder mid-air ends the jump. Disabled by XriRig while climbing,
    ///   XRI would otherwise keep the jump running and replay its rest after climbing.
    /// </summary>
    public class GothicJumpProvider : JumpProvider
    {
        // Constants of JumpProvider.CalculateJumpForceForFrame().
        private const float StartJumpForce = 7f;
        private const float EndJumpForce = 5f;
        private const float JumpHeightClamp = 4f;
        private const float MaxJumpHeight = 8f;

        private IVrLedgeHandler _handler;
        private float _gravity = 9.81f;
        private float _normalJumpHeight;
        private bool _isPressHandled;
        private bool _isPressConsumed;

        public void SetHandler(IVrLedgeHandler handler)
        {
            _handler = handler;
        }

        /// <summary>
        /// Gravity in m/s^2 the rise is calculated for. Set by XriRig, the owner of the rig's gravity.
        /// </summary>
        public float Gravity
        {
            get => _gravity;
            set => _gravity = value;
        }

        protected override void Awake()
        {
            base.Awake();
            _normalJumpHeight = jumpHeight;
        }

        protected override void Update()
        {
            var isPressed = jumpInput.ReadIsPerformed();
            if (!isPressed)
            {
                _isPressHandled = false;
                _isPressConsumed = false;
            }
            else if (!_isPressHandled && !isJumping && CanJump())
            {
                _isPressHandled = true;
                _isPressConsumed = _handler != null && _handler.TryHandleJump();
                if (!_isPressConsumed)
                    jumpHeight = GetTakeOffJumpHeight();
            }

            // XRI would jump on the consumed press as soon as it sees it.
            if (_isPressConsumed)
                return;

            base.Update();
        }

        /// <summary>
        /// Ends a running jump at once (as if the player landed).
        /// </summary>
        public void CancelJump()
        {
            if (isJumping && isActiveAndEnabled)
                OnGroundedChanged(true);
        }

        private float GetTakeOffJumpHeight()
        {
            var rise = _handler?.GetJumpRise() ?? 0f;
            if (rise <= 0f)
                return _normalJumpHeight;

            return Mathf.Max(_normalJumpHeight, GetJumpHeightForRise(rise, _gravity, maxJumpHoldTime,
                disableGravityDuringJump));
        }

        /// <summary>
        /// XRI's jumpHeight that lifts the player by rise (m). XRI pushes up with (1 - t/T) * h * k over the hold
        /// time T, k from 7 (low) to 5 (h >= 4), while gravity pulls down.
        /// </summary>
        public static float GetJumpHeightForRise(float rise, float gravity, float holdTime, bool isGravityDisabled)
        {
            var low = 0f;
            var high = MaxJumpHeight;
            for (var i = 0; i < 32; i++)
            {
                var middle = (low + high) * 0.5f;
                if (GetRise(middle, gravity, holdTime, isGravityDisabled) < rise)
                    low = middle;
                else
                    high = middle;
            }

            return high;
        }

        /// <summary>
        /// Apex (m above the take-off) of a jump with XRI's jumpHeight h.
        /// </summary>
        public static float GetRise(float jumpHeight, float gravity, float holdTime, bool isGravityDisabled)
        {
            var factor = Mathf.Lerp(StartJumpForce, EndJumpForce, Mathf.Clamp01(jumpHeight / JumpHeightClamp));
            if (isGravityDisabled)
                factor /= 1.5f;

            var push = jumpHeight * factor;
            if (holdTime <= 0f || push <= 0f)
                return 0f;

            // v(t) = push * (1 - t/T) - g * t is 0 at the apex, always before the push ends (at T only without
            // gravity).
            var g = isGravityDisabled ? 0f : gravity;
            var apexTime = push / (g + push / holdTime);
            return push * (apexTime - apexTime * apexTime / (2f * holdTime)) - g * apexTime * apexTime / 2f;
        }
    }
}
