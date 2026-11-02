using System;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// IVrRig on top of XRI's XR Origin and locomotion providers. Sits on the XR Origin GameObject next to VRPlayer.
    /// Sprinting and crouching are ours, as XRI has no providers for them.
    /// Gothic logic (menu, control modes, ini settings) lives in VRPlayer and VRContextInteractionService, not here.
    /// </summary>
    public class XriRig : MonoBehaviour, IVrRig
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly GameStateService _gameStateService;

        [Header("Rig")] [SerializeField] private XROrigin _origin;
        [SerializeField] private CharacterController _characterController;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _leftController;
        [SerializeField] private Transform _rightController;

        [Header("Grabbing")]
        [Tooltip("Near-Far interactor of the left hand (direct and distance grab, UI).")]
        [SerializeField]
        private XriGrabberBridge _leftGrabber;

        [Tooltip("Near-Far interactor of the right hand (direct and distance grab, UI).")] [SerializeField]
        private XriGrabberBridge _rightGrabber;

        [Tooltip("Fade on the camera, driven by Fade(). WallFade on the camera uses it too.")] [SerializeField]
        private ScreenFade _screenFade;

        [Header("Locomotion")] [SerializeField]
        private ContinuousMoveProvider _moveProvider;

        [SerializeField] private SnapTurnProvider _snapTurnProvider;
        [SerializeField] private ContinuousTurnProvider _continuousTurnProvider;
        [SerializeField] private TeleportationProvider _teleportationProvider;
        [SerializeField] private GravityProvider _gravityProvider;
        [SerializeField] private JumpProvider _jumpProvider;

        [Tooltip("Moves the rig while a hand holds a ClimbInteractable (ladders) and pauses gravity meanwhile.")]
        [SerializeField]
        private ClimbProvider _climbProvider;

        [Header("Teleport")] [Tooltip("Thumbstick teleport (arc + ring). Child of this GameObject.")] [SerializeField]
        private XriTeleportAimer _teleportAimer;

        [Header("Movement")] [Tooltip("Walking speed in m/s.")] [SerializeField]
        private float _moveSpeed = 1.5f;

        [Tooltip("Speed in m/s while sprinting (sprint button, simulator: Left Shift).")] [SerializeField]
        private float _runSpeed = 3.5f;

        [Tooltip("Thumbstick deflection below which a clicked sprint ends.")] [SerializeField]
        private float _sprintStopThreshold = 0.1f;

        [Tooltip("Gravity acceleration in m/s^2. 0 disables gravity (swimming, diving).")] [SerializeField]
        private float _gravity = 9.81f;

        [Tooltip("Terminal fall speed in m/s.")] [SerializeField]
        private float _maxFallSpeed = 20f;

        [Tooltip(
            "Keeps the player on the ground when walking downhill or down stairs (like HVR's constant downward push). "
            + "XRI's grounded check alone flickers between ground and air there.")]
        [SerializeField]
        private bool _snapToGround = true;

        [Tooltip("Max. drop in m per frame the player is pulled down to the ground (slopes, stairs). Larger drops "
                 + "(ledges) are real falls. Capped by the CharacterController's step offset.")]
        [SerializeField]
        private float _groundSnapDistance = 0.3f;

        [Tooltip(
            "Time in s the player still counts as grounded after losing ground contact. Hides one-frame flickers.")]
        [SerializeField]
        private float _groundedGraceTime = 0.12f;

        [Header("Height")]
        [Tooltip("Eye height of the virtual player in m. Used by the Sitting and Standing comfort settings.")]
        [SerializeField]
        private float _eyeHeight = 1.66f;

        [Tooltip("Camera height in m while crouching.")] [SerializeField]
        private float _crouchHeight = 0.7f;

        [Tooltip("Speed in m/s of the camera moving down/up when crouching starts/ends.")] [SerializeField]
        private float _crouchSpeed = 1.5f;

        [Tooltip("Below this HMD height (m), the Standing setting doesn't scale (no headset, not tracked yet).")]
        [SerializeField]
        private float _minCalibrationHeight = 0.5f;

        private XriInput _xriInput;
        private List<IVrGrabber> _handGrabbers;
        private Transform _cameraOffset;
        private bool _turnEnabled = true;
        private bool _teleportEnabled = true;
        private bool _canCrouch = true;
        private bool _canSprint = true;
        private VrTurnType _turnType = VrTurnType.Snap;
        private bool _isSprintLatched;
        private bool _isCrouching;
        private float _crouchOffset;
        private float _heightOffset;
        private bool _isMovementEnabled;
        private bool _isJumpEnabled;
        private float _lastGroundedTime = float.NegativeInfinity;
        private bool _isTeleportQueued;
        private int _lastTeleportFrame = -1;
        private Vector3 _teleportPosition;
        private XRBodyTransformer _bodyTransformer;
        private XriBodyManipulator _bodyManipulator;
        private readonly List<XRInputSubsystem> _inputSubsystems = new();
        private int _collidingLayers;
        private bool _isAirborneOnPurpose;

        public event Action<Vector3> Teleported;

        public Transform Root => _origin.Origin.transform;
        public Transform TrackingSpace => CameraOffset;
        public Transform Head => _camera.transform;
        public Transform LeftHand => _leftController;
        public Transform RightHand => _rightController;
        public Camera Camera => _camera;
        public CharacterController CharacterController => _characterController;
        public float CameraHeight => _origin.CameraInOriginSpaceHeight;

        public bool IsGrounded
        {
            get
            {
                if (_gravityProvider.isGrounded || _characterController.isGrounded)
                    _lastGroundedTime = Time.time;

                return Time.time - _lastGroundedTime <= _groundedGraceTime;
            }
        }

        /// <summary>
        /// A hand holds a ClimbInteractable. The ClimbProvider forces gravity off (GravityProvider lock) meanwhile.
        /// </summary>
        public bool IsClimbing => _climbProvider != null && _climbProvider.climbAnchorInteractable != null;

        public bool MovementEnabled
        {
            get => _isMovementEnabled;
            set
            {
                _isMovementEnabled = value;
                UpdateClimbLock();
            }
        }

        public bool TurnEnabled
        {
            get => _turnEnabled;
            set
            {
                _turnEnabled = value;
                UpdateTurnProviders();
            }
        }

        /// <summary>
        /// Player-initiated teleporting. Teleports from code (Teleport()) always work, therefore the
        /// TeleportationProvider itself stays enabled. Disabling only stops the thumbstick teleport (XriTeleportAimer).
        /// </summary>
        public bool TeleportEnabled
        {
            get => _teleportEnabled;
            set => _teleportEnabled = value;
        }

        public bool CanJump
        {
            get => _isJumpEnabled;
            set
            {
                _isJumpEnabled = value;
                UpdateClimbLock();
            }
        }

        public bool CanCrouch
        {
            get => _canCrouch;
            set
            {
                _canCrouch = value;
                if (!value)
                    _isCrouching = false;
            }
        }

        public bool CanSprint
        {
            get => _canSprint;
            set
            {
                _canSprint = value;
                if (!value)
                    _isSprintLatched = false;
            }
        }

        public float Gravity
        {
            get => _gravity;
            set
            {
                _gravity = value;
                ApplyGravity();
            }
        }

        public float MaxFallSpeed
        {
            get => _maxFallSpeed;
            set
            {
                _maxFallSpeed = value;
                ApplyGravity();
            }
        }

        public float MoveSpeed
        {
            get => _moveSpeed;
            set => _moveSpeed = value;
        }

        public float RunSpeed
        {
            get => _runSpeed;
            set => _runSpeed = value;
        }

        public float StepOffset
        {
            get => _characterController.stepOffset;
            set => _characterController.stepOffset = value;
        }

        public float FadeAlpha => _screenFade != null ? _screenFade.FadeAlpha : 0f;

        /// <summary>
        /// One Near-Far interactor per hand. Resolved lazily, as adapters on this GameObject ask in their Awake.
        /// </summary>
        public IReadOnlyList<IVrGrabber> HandGrabbers
        {
            get
            {
                if (_handGrabbers != null)
                    return _handGrabbers;

                _handGrabbers = new List<IVrGrabber>();
                AddGrabber(_leftGrabber, VrHandSide.Left);
                AddGrabber(_rightGrabber, VrHandSide.Right);
                return _handGrabbers;
            }
        }

        private Transform CameraOffset => _cameraOffset ??= _origin.CameraFloorOffsetObject.transform;

        private void Awake()
        {
            // Grounded check against everything the player collides with (world, VOBs, NPCs), not only Default.
            _collidingLayers = GetCollidingLayers(gameObject.layer);
            _gravityProvider.sphereCastLayerMask = _collidingLayers;

            _teleportationProvider.locomotionEnded += OnTeleportLocomotionEnded;

            _isMovementEnabled = _moveProvider.enabled;
            _isJumpEnabled = _jumpProvider.enabled;
            if (_climbProvider == null)
                Logger.LogWarning("[XRI] XriRig has no ClimbProvider. Ladders can't be climbed.", LogCat.VR);

            ApplyGravity();
            UpdateTurnProviders();
        }

        private void Start()
        {
            _xriInput = _vrInput as XriInput;
            if (_xriInput == null)
                Logger.LogError("XriRig needs the XRI backend (XriInput). Sprinting and crouching won't work.",
                    LogCat.VR);
            else
                CreateTeleportAimer();

            // Snapping has to run right after XRI moved the body this frame (see SnapToGround()).
            _bodyTransformer = _moveProvider.mediator != null ? _moveProvider.mediator.bodyTransformer : null;
            if (_bodyTransformer == null)
                _bodyTransformer = GetComponentInChildren<XRBodyTransformer>();

            if (_bodyTransformer != null)
            {
                _bodyManipulator = ScriptableObject.CreateInstance<XriBodyManipulator>();
                _bodyTransformer.beforeApplyTransformations += OnBeforeApplyTransformations;
                _bodyTransformer.afterApplyTransformations += OnAfterApplyTransformations;
            }
            else
            {
                Logger.LogError("[XRI] No XRBodyTransformer found. Walking downhill will bounce.", LogCat.VR);
            }
        }

        private void OnDestroy()
        {
            if (_teleportationProvider != null)
                _teleportationProvider.locomotionEnded -= OnTeleportLocomotionEnded;

            if (_bodyTransformer != null)
            {
                _bodyTransformer.beforeApplyTransformations -= OnBeforeApplyTransformations;
                _bodyTransformer.afterApplyTransformations -= OnAfterApplyTransformations;
            }

            if (_bodyManipulator != null)
                Destroy(_bodyManipulator);
        }

        private void Update()
        {
            UpdateClimbLock();
            UpdateSprint();
            UpdateCrouch();
        }

        /// <summary>
        /// Walking and jumping are off while climbing (the thumbstick would fight the climbing hands), and come
        /// back to what the game set (MovementEnabled/CanJump) afterwards.
        /// </summary>
        private void UpdateClimbLock()
        {
            var isClimbing = IsClimbing;
            _moveProvider.enabled = _isMovementEnabled && !isClimbing;
            _jumpProvider.enabled = _isJumpEnabled && !isClimbing;
        }

        /// <summary>
        /// The XRBodyTransformer takes XRI's own CharacterControllerBodyManipulator again whenever it gets enabled.
        /// Ours keeps the capsule above the feet (XriBodyManipulator), so it's set again before every body move.
        /// </summary>
        private void OnBeforeApplyTransformations(XRBodyTransformer bodyTransformer)
        {
            if (!ReferenceEquals(bodyTransformer.constrainedBodyManipulator, _bodyManipulator))
                bodyTransformer.constrainedBodyManipulator = _bodyManipulator;
        }

        private void OnAfterApplyTransformations(ApplyBodyTransformationsEventArgs args)
        {
            SnapToGround();
        }

        /// <summary>
        /// XRI moves horizontally only and GravityProvider pauses gravity while its head-down sphere cast still finds
        /// ground (up to ~4 cm below the feet). Walking downhill, the player therefore floats until that gap is
        /// exceeded, then gravity restarts from 0 and drops them a bit: the bouncing. Like HVR (constant downward push),
        /// we press the CharacterController onto the ground below it every frame after XRI's move (same frame,
        /// before rendering and before the next grounded check). Limited to the snap distance, so ledges and jumps
        /// stay real falls.
        /// </summary>
        private void SnapToGround()
        {
            if (!_snapToGround || _gravity <= 0f || _moveProvider.enableFly || !_characterController.enabled)
                return;

            // Teleports move the transform directly. A CharacterController.Move before the sync would undo them.
            if (_isTeleportQueued || _lastTeleportFrame == Time.frameCount)
                return;

            // From the jump or climb until landing, the player is in the air on purpose. Climbing moves the body
            // through XRI as well, snapping would pull the player back to the floor until the hand moved further
            // than the snap distance.
            if (_jumpProvider.isJumping || IsClimbing)
            {
                _isAirborneOnPurpose = true;
                return;
            }

            if (_isAirborneOnPurpose)
            {
                if (!_gravityProvider.isGrounded)
                    return;

                _isAirborneOnPurpose = false;
            }

            // Only snap if we were on the ground a moment ago. Otherwise we're falling for real.
            if (!IsGrounded)
                return;

            var up = Vector3.up;
            var skinWidth = _characterController.skinWidth;
            var radius = _characterController.radius * 0.5f;
            var bottom = _characterController.transform.TransformPoint(_characterController.center)
                         - up * (_characterController.height * 0.5f);
            var origin = bottom + up * (radius + skinWidth);
            var maxDistance = Mathf.Min(_groundSnapDistance, _characterController.stepOffset) + 2f * skinWidth;

            if (!Physics.SphereCast(origin, radius, -up, out var hit, maxDistance, _collidingLayers,
                    QueryTriggerInteraction.Ignore))
                return;

            // Too steep to stand on: let gravity slide the player down.
            if (Vector3.Angle(hit.normal, up) > _characterController.slopeLimit)
                return;

            // CharacterController.Move stops at the collision, overshooting by the skin width is safe and makes
            // CharacterController.isGrounded true.
            var drop = hit.distance - skinWidth;
            if (drop > 0f)
                _characterController.Move(-up * drop);
        }

        /// <summary>
        /// Right thumbstick forward aims, releasing teleports, grip cancels.
        /// </summary>
        private void CreateTeleportAimer()
        {
            var map = _xriInput.Actions?.FindActionMap("XRI Right Locomotion");
            var stickAction = map?.FindAction("Teleport Mode");
            if (stickAction == null)
            {
                Logger.LogError("[XRI] Action 'XRI Right Locomotion/Teleport Mode' not found. " +
                                "Teleporting with the thumbstick won't work.", LogCat.VR);
                return;
            }

            var cancelAction = map.FindAction("Teleport Mode Cancel");
            if (cancelAction == null)
                Logger.LogWarning("[XRI] Action 'XRI Right Locomotion/Teleport Mode Cancel' not found. " +
                                  "Aiming a teleport can't be canceled.", LogCat.VR);

            // While crouching, the CharacterController is lower than the player once standing up at the target.
            _teleportAimer.Init(stickAction, cancelAction, _rightController, _camera.transform, _characterController,
                GetCollidingLayers(gameObject.layer), () => _teleportEnabled, () => IsGrounded,
                () => CameraHeight + _crouchOffset, GetMaxSlopeAngle, Teleport);
        }

        /// <summary>
        /// Slide angle (degrees) of the human guild from Gothic's GIL_Values. Steeper ground makes Gothic's player
        /// slide. 0 if the script isn't loaded (the aimer then falls back to the CharacterController's slope limit).
        /// </summary>
        private float GetMaxSlopeAngle()
        {
            return _gameStateService?.GuildValues?.GetSlideAngle((int)VmGothicEnums.Guild.GIL_HUMAN) ?? 0;
        }

        private void UpdateSprint()
        {
            var sprintAction = _xriInput?.GetGothicAction(XriGothicAction.Sprint) ?? default;
            var moveInput = _moveProvider.leftHandMoveInput.ReadValue();

            // A click on the thumbstick sprints until the stick is released. Holding the button (simulator: Shift)
            // sprints as well.
            if (sprintAction.IsJustActivated)
                _isSprintLatched = true;
            else if (moveInput.magnitude < _sprintStopThreshold && !sprintAction.IsActive)
                _isSprintLatched = false;

            var isSprinting = _canSprint && (_isSprintLatched || sprintAction.IsActive);
            _moveProvider.moveSpeed = isSprinting ? _runSpeed : _moveSpeed;
        }

        private void UpdateCrouch()
        {
            if (_canCrouch && _xriInput != null && _xriInput.GetGothicAction(XriGothicAction.Crouch).IsJustActivated)
                _isCrouching = !_isCrouching;

            // The camera offset's height is owned by XROrigin (CameraYOffset in Device mode, 0 in Floor mode).
            // The player's height offset moves it up/down, crouching moves it down on top, so that the camera ends
            // at _crouchHeight. The CharacterController follows the camera height on its next move
            // (XriBodyManipulator).
            var baseOffset = GetTrackingOriginMode() == TrackingOriginModeFlags.Floor
                ? 0f
                : _origin.CameraYOffset;
            var standingCameraHeight = CameraHeight + _crouchOffset;
            var targetOffset = _isCrouching ? Mathf.Max(0f, standingCameraHeight - _crouchHeight) : 0f;
            _crouchOffset = Mathf.MoveTowards(_crouchOffset, targetOffset, _crouchSpeed * Time.deltaTime);

            var position = CameraOffset.localPosition;
            position.y = baseOffset + _heightOffset - _crouchOffset;
            CameraOffset.localPosition = position;
        }

        public void SetHeightOffset(float meters)
        {
            _heightOffset = meters;
        }

        /// <summary>
        /// XROrigin only takes over a new tracking origin when the XR provider raises trackingOriginUpdated. LinuxXR
        /// doesn't: Sitting (Device) stayed Floor for XROrigin, the eye height was missing and the camera ended at
        /// the floor. The running input subsystem knows the mode in use.
        /// </summary>
        private TrackingOriginModeFlags GetTrackingOriginMode()
        {
            SubsystemManager.GetSubsystems(_inputSubsystems);
            foreach (var inputSubsystem in _inputSubsystems)
            {
                if (inputSubsystem.running)
                    return inputSubsystem.GetTrackingOriginMode();
            }

            return _origin.CurrentTrackingOriginMode;
        }

        public void ApplyComfortSettings(VrSitStand sitStand, VrMoveDirection direction, VrTurnType turn,
            float snapDegrees, float smoothSpeed)
        {
            ApplySitStand(sitStand);

            _moveProvider.forwardSource = direction switch
            {
                VrMoveDirection.LeftController => _leftController,
                VrMoveDirection.RightController => _rightController,
                _ => _camera.transform
            };

            _turnType = turn;
            _snapTurnProvider.turnAmount = snapDegrees;
            _continuousTurnProvider.turnSpeed = smoothSpeed;
            UpdateTurnProviders();
        }

        /// <summary>
        /// Sitting: tracking relative to the HMD start position, lifted to the eye height.
        /// Standing: floor tracking, scaled so that the HMD's current height becomes the eye height (like HVR).
        /// PlayerHeight: floor tracking with the player's real height.
        /// </summary>
        private void ApplySitStand(VrSitStand sitStand)
        {
            var scale = 1f;

            switch (sitStand)
            {
                case VrSitStand.Sitting:
                    _origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
                    _origin.CameraYOffset = _eyeHeight;
                    break;
                case VrSitStand.Standing:
                    _origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                    var hmdHeight = _camera.transform.localPosition.y;
                    if (hmdHeight >= _minCalibrationHeight)
                        scale = _eyeHeight / hmdHeight;
                    break;
                default:
                    _origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                    break;
            }

            CameraOffset.localScale = Vector3.one * scale;
        }

        private static int GetCollidingLayers(int layer)
        {
            var mask = 0;
            for (var i = 0; i < 32; i++)
            {
                if (i != layer && !Physics.GetIgnoreLayerCollision(layer, i))
                    mask |= 1 << i;
            }

            return mask;
        }

        private void UpdateTurnProviders()
        {
            _snapTurnProvider.enabled = _turnEnabled && _turnType == VrTurnType.Snap;
            _continuousTurnProvider.enabled = _turnEnabled && _turnType == VrTurnType.Smooth;
        }

        private void ApplyGravity()
        {
            var physicsGravity = Physics.gravity.magnitude;
            _gravityProvider.useGravity = _gravity > 0f;
            _gravityProvider.gravityAccelerationModifier = physicsGravity > 0f ? _gravity / physicsGravity : 0f;
            _gravityProvider.terminalVelocity = _maxFallSpeed;

            if (_gravity <= 0f)
                _gravityProvider.ResetFallForce();
        }

        /// <summary>
        /// Applied by the TeleportationProvider in its next Update(). Teleported is raised once the XRBodyTransformer
        /// moved the rig (same frame). The camera (not the XR Origin) ends up above the position and looks along the rotation.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            // TeleportPlayerTo() passes default(Quaternion) (all 0) when no rotation is given.
            if (rotation == default)
                rotation = Quaternion.identity;

            ReleaseClimb();

            _teleportPosition = position;
            _isTeleportQueued = true;
            _gravityProvider.ResetFallForce();

            _teleportationProvider.QueueTeleportRequest(new TeleportRequest
            {
                destinationPosition = position,
                destinationRotation = rotation,
                matchOrientation = MatchOrientation.TargetUpAndForward,
                requestTime = Time.time
            });
        }

        /// <summary>
        /// The ClimbProvider keeps the hand at its grab point in world space, so it would pull a teleported rig back
        /// to the ladder. Hands holding a ladder therefore let go first.
        /// </summary>
        private void ReleaseClimb()
        {
            // Bounded: one ladder per hand at most.
            for (var i = 0; i < 2 && IsClimbing; i++)
            {
                var climbable = _climbProvider.climbAnchorInteractable;
                if (climbable.interactionManager == null)
                    return;

                climbable.interactionManager.CancelInteractableSelection((IXRSelectInteractable)climbable);
            }
        }

        /// <summary>
        /// XRI defers locomotionEnded until the body transformer applied the queued transformations.
        /// </summary>
        private void OnTeleportLocomotionEnded(LocomotionProvider provider)
        {
            if (!_isTeleportQueued)
                return;

            _isTeleportQueued = false;
            _lastTeleportFrame = Time.frameCount;

            // XRBodyGroundPosition moves the transform directly. Without the sync, the CharacterController would
            // continue from its old position (Physics.autoSyncTransforms is off).
            Physics.SyncTransforms();
            Teleported?.Invoke(_teleportPosition);
        }

        private void AddGrabber(XriGrabberBridge grabber, VrHandSide side)
        {
            if (grabber != null)
                _handGrabbers.Add(grabber);
            else
                Logger.LogError($"[XRI] XriRig has no grabber for the {side} hand. Grabbing won't work.", LogCat.VR);
        }

        /// <summary>
        /// Grabbing and hovering (direct and distance) of both hands. UI pointing keeps working.
        /// Disabling drops what the hands hold: XRI ends the selection of an interactor that may not select
        /// (swimming: as in G1, no items in the hands).
        /// </summary>
        public void SetGrabbingEnabled(bool isEnabled)
        {
            SetGrabbingEnabled(VrHandSide.Left, isEnabled);
            SetGrabbingEnabled(VrHandSide.Right, isEnabled);
        }

        public void SetGrabbingEnabled(VrHandSide side, bool isEnabled)
        {
            var grabber = side == VrHandSide.Left ? _leftGrabber : _rightGrabber;
            if (grabber == null)
                return;

            grabber.Interactor.allowSelect = isEnabled;
            grabber.Interactor.allowHover = isEnabled;
        }

        public bool IsGrabbingEnabled(VrHandSide side)
        {
            var grabber = side == VrHandSide.Left ? _leftGrabber : _rightGrabber;
            return grabber != null && grabber.Interactor.allowSelect;
        }

        public void Fade(float alpha, float speed)
        {
            if (_screenFade != null)
                _screenFade.Fade(alpha, speed);
        }
    }
}
