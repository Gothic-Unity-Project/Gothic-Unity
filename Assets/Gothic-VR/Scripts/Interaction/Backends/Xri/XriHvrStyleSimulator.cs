using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using CommonUsages = UnityEngine.InputSystem.CommonUsages;
using InputSystem = UnityEngine.InputSystem.InputSystem;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// Desktop simulator for the Xri backend with controls like the HVR simulator: W/A/S/D walk (left stick), right
    /// mouse looks, Q/E + mouse move the left/right hand. Replaces the input handling of XRI's XRInteractionSimulator;
    /// the simulated devices themselves (XRSimulatedHMD, XRSimulatedController) are still added by the
    /// SimulatedDeviceLifecycleManager next to it. This component writes their state every frame.
    /// Hand poses are kept relative to the head's yaw, so hands follow walking and turning but not looking up/down.
    /// </summary>
    [DefaultExecutionOrder(XRInteractionUpdateOrder.k_InteractionSimulator)]
    [RequireComponent(typeof(SimulatedDeviceLifecycleManager))]
    public class XriHvrStyleSimulator : MonoBehaviour
    {
        private const int _left = (int)VrHandSide.Left;
        private const int _right = (int)VrHandSide.Right;
        private const float _maxPitch = 85f;
        private const float _maxUiRayDistance = 30f;
        private const float _physicsRaySkipDistance = 0.5f;
        // A grip that grabbed nothing, or only used something (NPC talk, mobs), lets go after these frames once Q/E is
        // let go - the next G is a new press again instead of a release. While Q/E is held it stays (both grips).
        private const int _gripClickFrames = 5;

        [Tooltip("Degrees the head turns per pixel of mouse movement (right mouse held).")]
        public float LookSensitivity = 0.15f;

        [Tooltip("Meters a hand moves per pixel of mouse movement (Q/E held).")]
        public float HandMoveSensitivity = 0.0015f;

        [Tooltip("Meters a hand moves forward/back per scroll wheel notch (Q/E held).")]
        public float HandScrollStep = 0.03f;

        [Tooltip("Degrees a hand rotates per pixel of mouse movement (Q/E + middle mouse held).")]
        public float HandRotateSensitivity = 0.4f;

        [Tooltip("Degrees a hand rolls per scroll wheel notch (Q/E + middle mouse held).")]
        public float HandRollStep = 15f;

        [Tooltip("Multiplier for hand movement and rotation while Shift is held.")]
        public float PreciseFactor = 0.2f;

        [Tooltip("Hands can't move further away from the head than this (meters). Reaches the floor standing.")]
        public float MaxHandDistance = 1.8f;

        [Tooltip("Default (and reset) position of the left hand relative to the head, in the head's yaw frame.")]
        public Vector3 LeftHandDefaultPosition = new(-0.2f, -0.3f, 0.35f);

        [Tooltip("Default (and reset) position of the right hand relative to the head, in the head's yaw frame.")]
        public Vector3 RightHandDefaultPosition = new(0.2f, -0.3f, 0.35f);

        [Tooltip("Distance the right hand aims at when the mouse cursor points at neither UI nor a collider.")]
        public float CursorAimDefaultDistance = 3f;

        private readonly Vector3[] _handPositions = new Vector3[2];
        private readonly Quaternion[] _handRotations = new Quaternion[2];
        private readonly bool[] _isGripToggled = new bool[2];
        private readonly bool[] _isHandHeld = new bool[2];
        private readonly int[] _gripReleaseCheckFrames = { -1, -1 };

        // The hands' interactors read grip (select) and trigger (activate) from here directly (ManualValue): through
        // the simulated devices' input actions the grip didn't reach XRI's Select (talking, grabbing did nothing).
        private readonly NearFarInteractor[] _interactors = new NearFarInteractor[2];
        private readonly List<(XRInputButtonReader Reader, XRInputButtonReader.InputSourceMode Mode)> _savedReaders =
            new();
        private readonly List<RaycastResult> _uiRaycastResults = new();

        private XRSimulatedHMD _hmd;
        private readonly XRSimulatedController[] _controllers = new XRSimulatedController[2];
        private TrackedDeviceEventData _uiEventData;
        private Camera _camera;
        private float _headYaw;
        private float _headPitch;
        private bool _isCursorLocked;
        private bool _isSkippingMouseDelta;
        private int _uiLayerMask;

        /// <summary>
        /// The rig whose camera and tracking space the cursor aiming uses. Set by XriInput before enabling.
        /// </summary>
        public XriRig Rig { get; set; }

        /// <summary>
        /// The right hand points at the mouse cursor (UI laser) while it isn't moved, rotated or gripping.
        /// </summary>
        public bool IsCursorAimEnabled { get; private set; } = true;

        public bool IsLooking { get; private set; }

        public bool IsHandHeld(VrHandSide side) => _isHandHeld[(int)side];

        public bool IsGripToggled(VrHandSide side) => _isGripToggled[(int)side];

        private void OnEnable()
        {
            _uiLayerMask = LayerMask.GetMask("UI");
            ResetHands();
            Logger.Log("[XRI] HVR-style simulator enabled (F1: key list).", LogCat.VR);
        }

        private void OnDisable()
        {
            SetCursorLocked(false);
            RestoreInteractorInputs();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && mouse != null)
                ProcessInput(keyboard, mouse);

            ApplyDeviceStates(keyboard, mouse);
        }

        private void ProcessInput(Keyboard keyboard, Mouse mouse)
        {
            _isHandHeld[_left] = keyboard.qKey.isPressed;
            _isHandHeld[_right] = keyboard.eKey.isPressed;
            var isAnyHandHeld = _isHandHeld[_left] || _isHandHeld[_right];
            IsLooking = !isAnyHandHeld && mouse.rightButton.isPressed;

            SetCursorLocked(isAnyHandHeld || IsLooking);
            var mouseDelta = _isSkippingMouseDelta ? Vector2.zero : mouse.delta.ReadValue();
            _isSkippingMouseDelta = false;
            var scrollNotches = ToScrollNotches(mouse.scroll.ReadValue().y);
            var precision = keyboard.shiftKey.isPressed ? PreciseFactor : 1f;

            if (keyboard.rKey.wasPressedThisFrame)
                ResetHands();
            if (keyboard.cKey.wasPressedThisFrame)
                IsCursorAimEnabled = !IsCursorAimEnabled;

            if (IsLooking)
                Look(mouseDelta);

            var isRotating = mouse.middleButton.isPressed;
            for (var side = 0; side < 2; side++)
            {
                if (!_isHandHeld[side])
                    continue;

                if (keyboard.gKey.wasPressedThisFrame)
                    ToggleGrip(side);

                if (isRotating)
                    RotateHand(side, mouseDelta * precision, scrollNotches * precision);
                else
                    MoveHand(side, mouseDelta * precision, scrollNotches * precision);
            }

            for (var side = 0; side < 2; side++)
                ReleaseUnusedGrip(side);

            if (IsCursorAimEnabled && !_isHandHeld[_right] && !_isGripToggled[_right])
                AimRightHandAtCursor(mouse.position.ReadValue());
        }

        /// <summary>
        /// Like the HVR simulator, a grabbed item stays in the hand after letting go of Q/E, G again lets go.
        /// </summary>
        private void ToggleGrip(int side)
        {
            _isGripToggled[side] = !_isGripToggled[side];
            _gripReleaseCheckFrames[side] = _isGripToggled[side] ? Time.frameCount + _gripClickFrames : -1;
        }

        /// <summary>
        /// A grip press that grabbed nothing, or only used something (NPC talk, mobs: select only), lets go by itself
        /// once Q/E is let go. Otherwise every second G only released it.
        /// </summary>
        private void ReleaseUnusedGrip(int side)
        {
            if (!_isGripToggled[side] || _gripReleaseCheckFrames[side] < 0 ||
                Time.frameCount < _gripReleaseCheckFrames[side] || _isHandHeld[side])
                return;

            _gripReleaseCheckFrames[side] = -1;
            var held = GetHeld((VrHandSide)side);
            if (held == null || held.IsSelectOnly)
                _isGripToggled[side] = false;
        }

        private VrInteractable GetHeld(VrHandSide side)
        {
            if (Rig == null)
                return null;

            foreach (var grabber in Rig.HandGrabbers)
            {
                if (grabber.HandSide == side && grabber.Held != null)
                    return grabber.Held;
            }

            return null;
        }

        private void TakeOverInteractorInputs()
        {
            if (Rig == null || _savedReaders.Count > 0)
                return;

            foreach (var grabber in Rig.HandGrabbers)
            {
                if (grabber is not XriGrabberBridge bridge || bridge.Interactor == null)
                    continue;

                var interactor = bridge.Interactor;
                _interactors[(int)grabber.HandSide] = interactor;
                foreach (var reader in new[] { interactor.selectInput, interactor.activateInput })
                {
                    _savedReaders.Add((reader, reader.inputSourceMode));
                    reader.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
                }
            }
        }

        private void RestoreInteractorInputs()
        {
            foreach (var (reader, mode) in _savedReaders)
                reader.inputSourceMode = mode;
            _savedReaders.Clear();
            _interactors[_left] = _interactors[_right] = null;
        }

        private void QueueInteractorInput(int side, bool isGripping, bool isTrigger)
        {
            var interactor = _interactors[side];
            if (interactor == null)
                return;

            interactor.selectInput.QueueManualState(isGripping, isGripping ? 1f : 0f);
            interactor.activateInput.QueueManualState(isTrigger, isTrigger ? 1f : 0f);
        }

        private void Look(Vector2 mouseDelta)
        {
            _headYaw = Mathf.Repeat(_headYaw + mouseDelta.x * LookSensitivity, 360f);
            _headPitch = Mathf.Clamp(_headPitch - mouseDelta.y * LookSensitivity, -_maxPitch, _maxPitch);
        }

        /// <summary>
        /// Mouse moves the hand in the camera plane, the scroll wheel along the view direction.
        /// </summary>
        private void MoveHand(int side, Vector2 mouseDelta, float scrollNotches)
        {
            var pitchRotation = Quaternion.Euler(_headPitch, 0f, 0f);
            var delta = new Vector3(mouseDelta.x * HandMoveSensitivity, mouseDelta.y * HandMoveSensitivity,
                scrollNotches * HandScrollStep);
            _handPositions[side] = Vector3.ClampMagnitude(_handPositions[side] + pitchRotation * delta,
                MaxHandDistance);
        }

        /// <summary>
        /// Mouse x = yaw, mouse y = pitch, scroll wheel = roll.
        /// </summary>
        private void RotateHand(int side, Vector2 mouseDelta, float scrollNotches)
        {
            var rotation = _handRotations[side];
            var pitchAxis = Quaternion.Euler(_headPitch, 0f, 0f) * Vector3.right;
            rotation = Quaternion.AngleAxis(mouseDelta.x * HandRotateSensitivity, Vector3.up) * rotation;
            rotation = Quaternion.AngleAxis(-mouseDelta.y * HandRotateSensitivity, pitchAxis) * rotation;
            rotation *= Quaternion.AngleAxis(-scrollNotches * HandRollStep, Vector3.forward);
            _handRotations[side] = rotation;

            if (side == _right && mouseDelta != Vector2.zero)
                IsCursorAimEnabled = false;
        }

        private void ResetHands()
        {
            _handPositions[_left] = LeftHandDefaultPosition;
            _handPositions[_right] = RightHandDefaultPosition;
            _handRotations[_left] = Quaternion.identity;
            _handRotations[_right] = Quaternion.identity;
            IsCursorAimEnabled = true;
        }

        /// <summary>
        /// Point the right hand (and its UI laser) at what's under the mouse cursor: world-space UI, else a collider,
        /// else a point in front of the camera.
        /// </summary>
        private void AimRightHandAtCursor(Vector2 screenPosition)
        {
            if (Rig == null)
                return;
            if (_camera == null)
                _camera = Rig.Head.GetComponent<Camera>();
            if (_camera == null)
                return;

            var ray = _camera.ScreenPointToRay(screenPosition);
            var distance = CursorAimDefaultDistance;
            if (TryRaycastUi(ray, out var uiDistance))
                distance = uiDistance;
            else if (Physics.Raycast(ray.origin + ray.direction * _physicsRaySkipDistance, ray.direction, out var hit,
                         _maxUiRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                distance = hit.distance + _physicsRaySkipDistance;

            var target = Rig.TrackingSpace.InverseTransformPoint(ray.GetPoint(distance));
            var yawRotation = Quaternion.Euler(0f, _headYaw, 0f);
            var direction = target - yawRotation * _handPositions[_right];
            if (direction.sqrMagnitude < 0.0001f)
                return;

            var rotation = Quaternion.LookRotation(direction, yawRotation * Vector3.up);
            _handRotations[_right] = Quaternion.Inverse(yawRotation) * rotation;
        }

        private bool TryRaycastUi(Ray ray, out float distance)
        {
            distance = 0f;
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            _uiEventData ??= new TrackedDeviceEventData(eventSystem) { rayPoints = new List<Vector3>(2) };
            _uiEventData.rayPoints.Clear();
            _uiEventData.rayPoints.Add(ray.origin);
            _uiEventData.rayPoints.Add(ray.GetPoint(_maxUiRayDistance));
            _uiEventData.layerMask = _uiLayerMask;

            _uiRaycastResults.Clear();
            eventSystem.RaycastAll(_uiEventData, _uiRaycastResults);
            foreach (var result in _uiRaycastResults)
            {
                if (!result.isValid || result.distance <= 0f)
                    continue;

                distance = Vector3.Distance(ray.origin, result.worldPosition);
                return true;
            }

            return false;
        }

        private void ApplyDeviceStates(Keyboard keyboard, Mouse mouse)
        {
            TakeOverInteractorInputs();

            var headRotation = Quaternion.Euler(_headPitch, _headYaw, 0f);
            var yawRotation = Quaternion.Euler(0f, _headYaw, 0f);

            if (_hmd == null || !_hmd.added)
                _hmd = InputSystem.GetDevice<XRSimulatedHMD>();
            if (_hmd != null)
                InputState.Change(_hmd, CreateHmdState(headRotation));

            for (var side = 0; side < 2; side++)
            {
                var controller = _controllers[side];
                if (controller == null || !controller.added)
                {
                    controller = InputSystem.GetDevice<XRSimulatedController>(side == _left
                        ? CommonUsages.LeftHand
                        : CommonUsages.RightHand);
                    _controllers[side] = controller;
                }

                if (controller != null)
                    InputState.Change(controller, CreateControllerState(side, yawRotation, keyboard, mouse));
            }
        }

        private static XRSimulatedHMDState CreateHmdState(Quaternion headRotation)
        {
            var state = new XRSimulatedHMDState();
            state.Reset();
            state.centerEyeRotation = headRotation;
            state.leftEyeRotation = headRotation;
            state.rightEyeRotation = headRotation;
            state.deviceRotation = headRotation;
            state.isTracked = true;
            state.trackingState = (int)(InputTrackingState.Position | InputTrackingState.Rotation);
            return state;
        }

        private XRSimulatedControllerState CreateControllerState(int side, Quaternion yawRotation, Keyboard keyboard,
            Mouse mouse)
        {
            var state = new XRSimulatedControllerState();
            state.Reset();
            state.devicePosition = yawRotation * _handPositions[side];
            state.deviceRotation = yawRotation * _handRotations[side];
            state.isTracked = true;
            state.trackingState = (int)(InputTrackingState.Position | InputTrackingState.Rotation);
            state.batteryLevel = 1f;

            var isGripping = _isGripToggled[side];
            state.grip = isGripping ? 1f : 0f;
            state.WithButton(ControllerButton.GripButton, isGripping);

            if (keyboard == null || mouse == null)
            {
                QueueInteractorInput(side, isGripping, false);
                return state;
            }

            // Buttons go to the held hand(s). With no hand held they go to the right hand (UI click, video skip).
            var isAnyHandHeld = _isHandHeld[_left] || _isHandHeld[_right];
            var isTarget = _isHandHeld[side] || (!isAnyHandHeld && side == _right);
            var isTrigger = isTarget && mouse.leftButton.isPressed;
            var isPrimary = isTarget && keyboard.digit1Key.isPressed;
            var isSecondary = isTarget && keyboard.digit2Key.isPressed;
            var isStickClick = isTarget && keyboard.digit3Key.isPressed;

            QueueInteractorInput(side, isGripping, isTrigger);
            state.trigger = isTrigger ? 1f : 0f;
            state.WithButton(ControllerButton.TriggerButton, isTrigger);
            state.WithButton(ControllerButton.PrimaryButton, isPrimary);
            state.WithButton(ControllerButton.PrimaryTouch, isPrimary);
            state.WithButton(ControllerButton.SecondaryButton, isSecondary);
            state.WithButton(ControllerButton.SecondaryTouch, isSecondary);
            state.WithButton(ControllerButton.Primary2DAxisClick, isStickClick);

            state.primary2DAxis = side == _left
                ? ReadStick(keyboard.wKey, keyboard.sKey, keyboard.aKey, keyboard.dKey)
                : ReadStick(keyboard.iKey, keyboard.kKey, keyboard.jKey, keyboard.lKey);
            state.WithButton(ControllerButton.Primary2DAxisTouch,
                isStickClick || state.primary2DAxis != Vector2.zero);

            return state;
        }

        private static Vector2 ReadStick(KeyControl up, KeyControl down, KeyControl left, KeyControl right)
        {
            var stick = new Vector2((right.isPressed ? 1f : 0f) - (left.isPressed ? 1f : 0f),
                (up.isPressed ? 1f : 0f) - (down.isPressed ? 1f : 0f));
            return Vector2.ClampMagnitude(stick, 1f);
        }

        /// <summary>
        /// Windows reports 120 per wheel notch, other platforms (or the uniform scroll setting) 1.
        /// </summary>
        private static float ToScrollNotches(float scroll)
        {
            return Mathf.Abs(scroll) >= 10f ? scroll / 120f : scroll;
        }

        /// <summary>
        /// The cursor is locked while looking or moving a hand, so the mouse doesn't leave the window. The frame it
        /// changes reports a jump in the mouse delta, which is skipped.
        /// </summary>
        private void SetCursorLocked(bool isLocked)
        {
            if (_isCursorLocked == isLocked)
                return;

            _isCursorLocked = isLocked;
            _isSkippingMouseDelta = true;
            Cursor.lockState = isLocked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !isLocked;
        }
    }
}
