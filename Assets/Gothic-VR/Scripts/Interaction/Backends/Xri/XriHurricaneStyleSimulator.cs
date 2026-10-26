using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// DeveloperConfig.VrSimulatorControls HurricaneStyle: keyboard and mouse like the HVR simulator, on the XRI
    /// backend. It writes the states of the XR Interaction Simulator's simulated devices (HMD and both controllers,
    /// added by its SimulatedDeviceLifecycleManager) itself - the XRInteractionSimulator component (XRI's own keys)
    /// stays disabled. Everything that reads XR controllers (XRI's interactors, GothicVR.inputactions) sees them.
    ///
    /// - W/A/S/D walk (left stick), Shift runs, right mouse button + mouse turns (body) and looks up/down (head).
    /// - Hold Q (left) / E (right) and move the mouse: the hand moves, scroll wheel forward/back, middle mouse
    ///   button rotates it, Shift slows it down.
    /// - While holding Q/E: G grabs / lets go (toggle, like HVR), left click is the trigger, 1/2/3 primary/secondary/
    ///   stick button. Without Q/E a left click is the right hand's trigger (UI laser).
    /// - Space jump, X crouch, Esc menu, T both grips: the "Gothic" map's simulator keys (XriInput).
    /// </summary>
    public class XriHurricaneStyleSimulator : MonoBehaviour
    {
        private const float _eyeHeight = 1.6f;
        private const float _turnSpeed = 0.1f;
        private const float _handMoveSpeed = 0.1f;
        private const float _handRotateSpeed = 20f;
        private const float _slowDownRate = 0.05f;
        private const float _scrollToMeters = 100f;
        private const float _maxPitch = 85f;
        // Relative to the head's position (not its rotation): the hands go with the eyes' height, wherever the comfort
        // settings put them.
        private static readonly Vector3 _leftHandStart = new(-0.15f, -0.5f, 0.35f);
        private static readonly Vector3 _rightHandStart = new(0.15f, -0.5f, 0.35f);
        // A grip press that grabbed nothing (or only used an NPC/mob) is let go after these frames - the next G press
        // is a new press again.
        private const int _gripClickFrames = 5;

        private XriRig _rig;
        private float _pitch;
        private readonly HandState[] _hands = { new(_leftHandStart), new(_rightHandStart) };
        private bool _isHelpShown = true;

        // The hands' interactors read grab (select) and trigger (activate) from us directly (ManualValue) - through
        // the simulated devices' input actions the grab never arrived. Index VrHandSide; restored on disable.
        private readonly NearFarInteractor[] _interactors = new NearFarInteractor[2];
        private readonly List<(XRInputButtonReader Reader, XRInputButtonReader.InputSourceMode Mode)> _savedReaders = new();

        private class HandState
        {
            // Offset from the head's position in tracking space.
            public Vector3 Position;
            public Quaternion Rotation = Quaternion.identity;
            public bool IsGripLatched;
            public int GripReleaseCheckFrame = -1;

            public HandState(Vector3 position)
            {
                Position = position;
            }
        }

        public void Init(XriRig rig)
        {
            _rig = rig;
            TakeOverInteractorInputs();
            Logger.Log("[XRI] Hurricane style simulator controls.", LogCat.VR);
        }

        private void TakeOverInteractorInputs()
        {
            RestoreInteractorInputs();
            if (_rig == null)
                return;

            foreach (var grabber in _rig.HandGrabbers)
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
            _interactors[0] = _interactors[1] = null;
        }

        private void OnEnable()
        {
            if (_rig != null && _savedReaders.Count == 0)
                TakeOverInteractorInputs();
        }

        /// <summary>
        /// The HMD's height in tracking space that puts the eyes at _eyeHeight: the comfort settings (Gothic.ini) lift
        /// it (Sitting: CameraYOffset) or scale it (Standing) later - a fixed 1.6 m made the hero a giant.
        /// Crouching lowers the camera offset on top, so it still works.
        /// </summary>
        private float GetHmdHeight()
        {
            if (_rig == null)
                return _eyeHeight;

            var scale = Mathf.Max(0.01f, _rig.TrackingScale);
            return Mathf.Max(0f, (_eyeHeight - _rig.TrackingBaseOffset) / scale);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var hmd = InputSystem.devices.OfType<XRSimulatedHMD>().FirstOrDefault(i => i.added);
            var left = InputSystem.GetDevice<XRSimulatedController>(UnityEngine.InputSystem.CommonUsages.LeftHand);
            var right = InputSystem.GetDevice<XRSimulatedController>(UnityEngine.InputSystem.CommonUsages.RightHand);
            if (keyboard == null || mouse == null || hmd == null || left == null || right == null)
                return;

            if (keyboard.f1Key.wasPressedThisFrame)
                _isHelpShown = !_isHelpShown;

            var isLeftHand = keyboard.qKey.isPressed;
            var isRightHand = keyboard.eKey.isPressed;
            var isSlow = keyboard.leftShiftKey.isPressed;
            var mouseDelta = mouse.delta.ReadValue();
            var scroll = mouse.scroll.ReadValue().y;

            UpdateHead(mouse, mouseDelta, isLeftHand || isRightHand);
            if (isLeftHand)
                UpdateHand(_hands[0], mouse, mouseDelta, scroll, isSlow);
            if (isRightHand)
                UpdateHand(_hands[1], mouse, mouseDelta, scroll, isSlow);

            var headPosition = new Vector3(0f, GetHmdHeight(), 0f);
            var head = new XRSimulatedHMDState
            {
                devicePosition = headPosition,
                deviceRotation = Quaternion.Euler(_pitch, 0f, 0f),
                isTracked = true,
                trackingState = 3
            };
            head.centerEyePosition = head.devicePosition;
            head.centerEyeRotation = head.deviceRotation;
            head.leftEyePosition = head.devicePosition;
            head.leftEyeRotation = head.deviceRotation;
            head.rightEyePosition = head.devicePosition;
            head.rightEyeRotation = head.deviceRotation;
            InputState.Change(hmd, head);

            // Walking and running with the left stick (XRI's move provider), whichever hand is moved.
            var move = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            var isRunning = isSlow && !isLeftHand && !isRightHand && move.sqrMagnitude > 0f;
            var noHand = !isLeftHand && !isRightHand;

            InputState.Change(left, CreateControllerState(_hands[0], VrHandSide.Left, headPosition, isLeftHand,
                keyboard, mouse, false, move, isRunning));
            InputState.Change(right, CreateControllerState(_hands[1], VrHandSide.Right, headPosition, isRightHand,
                keyboard, mouse, noHand, Vector2.zero, false));
        }

        /// <summary>
        /// Right mouse button: the body turns with the mouse's X (around the head), the head looks up/down with its Y.
        /// </summary>
        private void UpdateHead(Mouse mouse, Vector2 mouseDelta, bool isHandMoving)
        {
            var isTurning = mouse.rightButton.isPressed && !isHandMoving;
            Cursor.visible = !isTurning;
            if (!isTurning || _rig == null)
                return;

            var root = _rig.Root;
            var pivot = _rig.Head != null ? _rig.Head.position : root.position;
            root.RotateAround(pivot, Vector3.up, mouseDelta.x * _turnSpeed);
            _pitch = Mathf.Clamp(_pitch - mouseDelta.y * _turnSpeed, -_maxPitch, _maxPitch);
        }

        /// <summary>
        /// Mouse moves the hand in the view's plane, the scroll wheel forward/back; middle mouse button rotates it.
        /// </summary>
        private void UpdateHand(HandState hand, Mouse mouse, Vector2 mouseDelta, float scroll, bool isSlow)
        {
            var factor = isSlow ? _slowDownRate : 1f;
            if (mouse.middleButton.isPressed)
            {
                var rotation = new Vector3(-mouseDelta.y, mouseDelta.x, scroll * _scrollToMeters) *
                               (_handRotateSpeed * factor * Time.deltaTime);
                hand.Rotation *= Quaternion.Euler(rotation);
                return;
            }

            if (_rig == null || _rig.Head == null || _rig.TrackingSpace == null)
                return;

            var direction = new Vector3(mouseDelta.x, mouseDelta.y, scroll * _scrollToMeters);
            var world = _rig.Head.rotation * direction * (_handMoveSpeed * factor * Time.deltaTime);
            hand.Position += _rig.TrackingSpace.InverseTransformDirection(world);
        }

        private XRSimulatedControllerState CreateControllerState(HandState hand, VrHandSide side, Vector3 headPosition,
            bool isControlled, Keyboard keyboard, Mouse mouse, bool isLaserClick, Vector2 stick, bool isStickClick)
        {
            UpdateGrip(hand, side, isControlled && keyboard.gKey.wasPressedThisFrame,
                isControlled && keyboard.gKey.isPressed);

            var isTrigger = (isControlled || isLaserClick) && mouse.leftButton.isPressed;
            var state = new XRSimulatedControllerState
            {
                devicePosition = headPosition + hand.Position,
                deviceRotation = hand.Rotation,
                isTracked = true,
                trackingState = 3,
                primary2DAxis = stick,
                grip = hand.IsGripLatched ? 1f : 0f,
                trigger = isTrigger ? 1f : 0f
            };
            var interactor = _interactors[(int)side];
            if (interactor != null)
            {
                interactor.selectInput.QueueManualState(hand.IsGripLatched, hand.IsGripLatched ? 1f : 0f);
                interactor.activateInput.QueueManualState(isTrigger, isTrigger ? 1f : 0f);
            }

            state = state
                .WithButton(ControllerButton.GripButton, hand.IsGripLatched)
                .WithButton(ControllerButton.TriggerButton, isTrigger)
                .WithButton(ControllerButton.PrimaryButton, isControlled && keyboard.digit1Key.isPressed)
                .WithButton(ControllerButton.SecondaryButton, isControlled && keyboard.digit2Key.isPressed)
                .WithButton(ControllerButton.Primary2DAxisClick,
                    isStickClick || (isControlled && keyboard.digit3Key.isPressed));
            return state;
        }

        /// <summary>
        /// Like the HVR simulator, a grabbed item stays in the hand after letting go of Q/E, G again lets go. A press
        /// that grabbed nothing, or only used something (NPC talk, mobs), is let go a few frames later - but not while G
        /// is still held: drawing a bow's string holds no item, the string snapped back at once.
        /// </summary>
        private void UpdateGrip(HandState hand, VrHandSide side, bool isPressed, bool isKeyHeld)
        {
            if (isPressed)
            {
                hand.IsGripLatched = !hand.IsGripLatched;
                hand.GripReleaseCheckFrame = hand.IsGripLatched ? Time.frameCount + _gripClickFrames : -1;
                var interactor = _interactors[(int)side];
                var hovered = interactor != null && interactor.interactablesHovered.Count > 0
                    ? ((Component)interactor.interactablesHovered[0]).name
                    : "nothing";
                Logger.Log($"[XRI] Simulator: {side} grip {(hand.IsGripLatched ? "pressed" : "let go")} " +
                           $"(hovering {hovered})", LogCat.VR);
                return;
            }

            if (!hand.IsGripLatched || hand.GripReleaseCheckFrame < 0 || Time.frameCount < hand.GripReleaseCheckFrame ||
                isKeyHeld)
                return;

            hand.GripReleaseCheckFrame = -1;
            var held = GetHeld(side);
            if (held == null || held.IsSelectOnly)
                hand.IsGripLatched = false;
        }

        private Components.VrInteractable GetHeld(VrHandSide side)
        {
            if (_rig == null)
                return null;

            foreach (var grabber in _rig.HandGrabbers)
            {
                if (grabber.HandSide == side && grabber.Held != null)
                    return grabber.Held;
            }
            return null;
        }

        private void OnDisable()
        {
            RestoreInteractorInputs();
            Cursor.visible = true;
        }

        private void OnGUI()
        {
            if (!_isHelpShown)
                return;

            const string text =
                "<b>Body</b>\nMove -> W/A/S/D, run -> Shift\nTurn/look -> hold right mouse button\n" +
                "Jump -> Space, crouch -> X, menu -> Esc\n\n" +
                "<b>Hands</b>\nMove left/right hand -> hold Q / E + mouse, scroll = forward/back\n" +
                "Rotate -> + middle mouse button, slow -> + Shift\n" +
                "Grab/let go -> + G, trigger -> + left click (alone: right hand's laser)\n" +
                "Primary/secondary/stick button -> + 1/2/3, both grips -> T, spell -> R\n" +
                "Help on/off -> F1";
            GUI.Box(new Rect(1, Screen.height - 215, 430, 210), GUIContent.none);
            GUI.Label(new Rect(8, Screen.height - 210, 420, 200), text);
        }
    }
}
