using System;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
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

        [Header("Rig")]
        [SerializeField] private XROrigin _origin;
        [SerializeField] private CharacterController _characterController;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _leftController;
        [SerializeField] private Transform _rightController;

        [Header("Grabbing")]
        [Tooltip("Near-Far interactor of the left hand (direct and distance grab, UI).")]
        [SerializeField] private XriGrabberBridge _leftGrabber;

        [Tooltip("Near-Far interactor of the right hand (direct and distance grab, UI).")]
        [SerializeField] private XriGrabberBridge _rightGrabber;

        [Tooltip("Fade on the camera, driven by Fade(). WallFade on the camera uses it too.")]
        [SerializeField] private ScreenFade _screenFade;

        [Header("Locomotion")]
        [SerializeField] private ContinuousMoveProvider _moveProvider;
        [SerializeField] private SnapTurnProvider _snapTurnProvider;
        [SerializeField] private ContinuousTurnProvider _continuousTurnProvider;
        [SerializeField] private TeleportationProvider _teleportationProvider;
        [SerializeField] private GravityProvider _gravityProvider;
        [SerializeField] private JumpProvider _jumpProvider;

        [Header("Movement")]
        [Tooltip("Walking speed in m/s.")]
        [SerializeField] private float _moveSpeed = 1.5f;

        [Tooltip("Speed in m/s while sprinting (sprint button, simulator: Left Shift).")]
        [SerializeField] private float _runSpeed = 3.5f;

        [Tooltip("Thumbstick deflection below which a clicked sprint ends.")]
        [SerializeField] private float _sprintStopThreshold = 0.1f;

        [Tooltip("Gravity acceleration in m/s^2. 0 disables gravity (swimming, diving).")]
        [SerializeField] private float _gravity = 9.81f;

        [Tooltip("Terminal fall speed in m/s.")]
        [SerializeField] private float _maxFallSpeed = 20f;

        [Header("Height")]
        [Tooltip("Eye height of the virtual player in m. Used by the Sitting and Standing comfort settings.")]
        [SerializeField] private float _eyeHeight = 1.66f;

        [Tooltip("Camera height in m while crouching.")]
        [SerializeField] private float _crouchHeight = 0.7f;

        [Tooltip("Speed in m/s of the camera moving down/up when crouching starts/ends.")]
        [SerializeField] private float _crouchSpeed = 1.5f;

        [Tooltip("Below this HMD height (m), the Standing setting doesn't scale (no headset, not tracked yet).")]
        [SerializeField] private float _minCalibrationHeight = 0.5f;

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
        private bool _isTeleportQueued;
        private Vector3 _teleportPosition;

        public event Action<Vector3> Teleported;

        public Transform Root => _origin.Origin.transform;
        public Transform TrackingSpace => CameraOffset;
        public Transform Head => _camera.transform;
        public Transform LeftHand => _leftController;
        public Transform RightHand => _rightController;
        public Camera Camera => _camera;
        public CharacterController CharacterController => _characterController;
        public float CameraHeight => _origin.CameraInOriginSpaceHeight;
        public bool IsGrounded => _gravityProvider.isGrounded || _characterController.isGrounded;

        // Climbing comes with HVR phase 4 (ClimbProvider).
        public bool IsClimbing => false;

        public bool MovementEnabled
        {
            get => _moveProvider.enabled;
            set => _moveProvider.enabled = value;
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
        /// TeleportationProvider itself stays enabled. There are no teleport interactors yet (HVR phase 2).
        /// </summary>
        public bool TeleportEnabled
        {
            get => _teleportEnabled;
            set => _teleportEnabled = value;
        }

        public bool CanJump
        {
            get => _jumpProvider.enabled;
            set => _jumpProvider.enabled = value;
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
            _gravityProvider.sphereCastLayerMask = GetCollidingLayers(gameObject.layer);

            _teleportationProvider.locomotionEnded += OnTeleportLocomotionEnded;

            ApplyGravity();
            UpdateTurnProviders();
        }

        private void Start()
        {
            _xriInput = _vrInput as XriInput;
            if (_xriInput == null)
                Logger.LogError("XriRig needs the XRI backend (XriInput). Sprinting and crouching won't work.",
                    LogCat.VR);
        }

        private void OnDestroy()
        {
            if (_teleportationProvider != null)
                _teleportationProvider.locomotionEnded -= OnTeleportLocomotionEnded;
        }

        private void Update()
        {
            UpdateSprint();
            UpdateCrouch();
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
            // Crouching moves it down on top, so that the camera ends at _crouchHeight. The CharacterController
            // follows the camera height on its next move (CharacterControllerBodyManipulator).
            var baseOffset = _origin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Floor
                ? 0f
                : _origin.CameraYOffset;
            var standingCameraHeight = CameraHeight + _crouchOffset;
            var targetOffset = _isCrouching ? Mathf.Max(0f, standingCameraHeight - _crouchHeight) : 0f;
            _crouchOffset = Mathf.MoveTowards(_crouchOffset, targetOffset, _crouchSpeed * Time.deltaTime);

            var position = CameraOffset.localPosition;
            position.y = baseOffset - _crouchOffset;
            CameraOffset.localPosition = position;
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
        /// XRI defers locomotionEnded until the body transformer applied the queued transformations.
        /// </summary>
        private void OnTeleportLocomotionEnded(LocomotionProvider provider)
        {
            if (!_isTeleportQueued)
                return;

            _isTeleportQueued = false;

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
        /// Objects already held stay in the hand.
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

        public void Fade(float alpha, float speed)
        {
            if (_screenFade != null)
                _screenFade.Fade(alpha, speed);
        }
    }
}
