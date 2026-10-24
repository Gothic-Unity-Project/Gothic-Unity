using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// Controller input from GothicVR.inputactions (maps "Gothic Left", "Gothic Right" and "Gothic").
    /// The XRI maps of the same asset are enabled by the XRI rig's components, not here.
    /// </summary>
    public class XriInput : IVrInput
    {
        public const string InputActionsResourcePath = "VR/Input/GothicVR";

        private const string _leftHandMapName = "Gothic Left";
        private const string _rightHandMapName = "Gothic Right";
        private const string _gothicMapName = "Gothic";
        private const string _velocityActionName = "Velocity";
        private const string _hapticActionName = "Haptic";

        // Binding groups of the "Gothic" map: controller bindings ("XR") are always used, the keyboard shortcuts
        // ("Simulator") only while the XR Interaction Simulator is active.
        private const string _xrBindingGroup = "XR";

        // Indexed by VrButton.
        private static readonly string[] _buttonActionNames =
        {
            "Grip", "Trigger", "Primary", "Secondary", "Menu", "Joystick Button"
        };

        // Indexed by XriGothicAction.
        private static readonly string[] _gothicActionNames = { "Menu", "Jump", "Crouch", "Sprint" };

        private readonly InputAction[,] _buttons = new InputAction[2, _buttonActionNames.Length];
        private readonly InputAction[] _velocities = new InputAction[2];
        private readonly InputAction[] _haptics = new InputAction[2];
        private readonly InputAction[] _gothicActions = new InputAction[_gothicActionNames.Length];

        // Simulator only: velocity from the controller's position change, as simulated devices have no velocity.
        private readonly Vector3[] _simulatedVelocities = new Vector3[2];
        private readonly Vector3[] _simulatedLastPositions = new Vector3[2];
        private readonly int[] _simulatedVelocityFrames = { -1, -1 };

        private InputActionMap _gothicMap;
        private XRInteractionSimulator _simulator;
        private XriRig _simulatorRig;
        private bool _isSimulatorActive;

        public InputActionAsset Actions { get; }

        public bool IsSimulator => _isSimulatorActive && _simulator != null && _simulator.isActiveAndEnabled;

        public bool UsesBothGripsSimulatorKey => false;

        public XriInput()
        {
            Actions = Resources.Load<InputActionAsset>(InputActionsResourcePath);
            if (Actions == null)
            {
                Logger.LogError($"XRI input actions not found at Resources/{InputActionsResourcePath}. " +
                                "VR controller input won't work.", LogCat.VR);
                return;
            }

            InitHand(VrHandSide.Left, _leftHandMapName);
            InitHand(VrHandSide.Right, _rightHandMapName);

            _gothicMap = Actions.FindActionMap(_gothicMapName, true);
            for (var i = 0; i < _gothicActionNames.Length; i++)
                _gothicActions[i] = _gothicMap.FindAction(_gothicActionNames[i], true);
            ApplyKeyboardShortcuts(false);
            _gothicMap.Enable();
        }

        private void InitHand(VrHandSide side, string mapName)
        {
            var map = Actions.FindActionMap(mapName, true);
            for (var i = 0; i < _buttonActionNames.Length; i++)
                _buttons[(int)side, i] = map.FindAction(_buttonActionNames[i], true);

            _velocities[(int)side] = map.FindAction(_velocityActionName, true);
            _haptics[(int)side] = map.FindAction(_hapticActionName, true);
            map.Enable();
        }

        public VrButtonState GetButton(VrHandSide side, VrButton button)
        {
            return ToState(_buttons[(int)side, (int)button]);
        }

        /// <summary>
        /// Gothic-specific actions (jump, crouch, sprint, menu) incl. their simulator keys.
        /// </summary>
        public VrButtonState GetGothicAction(XriGothicAction action)
        {
            return ToState(_gothicActions[(int)action]);
        }

        public Vector3 GetControllerVelocity(VrHandSide side)
        {
            if (IsSimulator && _simulatorRig != null)
                return GetSimulatedControllerVelocity(side);

            var action = _velocities[(int)side];
            return action == null ? Vector3.zero : action.ReadValue<Vector3>();
        }

        /// <summary>
        /// The XR Interaction Simulator's controllers report no velocity (swim strokes would do nothing). Derived from
        /// the controller's position in the tracking space between two frames instead. Zero on the first call after a
        /// pause, as there is nothing to compare against.
        /// </summary>
        private Vector3 GetSimulatedControllerVelocity(VrHandSide side)
        {
            var index = (int)side;
            if (_simulatedVelocityFrames[index] == Time.frameCount)
                return _simulatedVelocities[index];

            var hand = side == VrHandSide.Left ? _simulatorRig.LeftHand : _simulatorRig.RightHand;
            var position = hand.localPosition;
            var isConsecutiveFrame = _simulatedVelocityFrames[index] == Time.frameCount - 1;
            _simulatedVelocities[index] = isConsecutiveFrame && Time.deltaTime > 0f
                ? (position - _simulatedLastPositions[index]) / Time.deltaTime
                : Vector3.zero;
            _simulatedLastPositions[index] = position;
            _simulatedVelocityFrames[index] = Time.frameCount;

            return _simulatedVelocities[index];
        }

        /// <summary>
        /// Pass-through action bound to the controller device, used to send haptic impulses.
        /// </summary>
        public InputAction GetHapticAction(VrHandSide side)
        {
            return _haptics[(int)side];
        }

        public void SetDeviceSimulatorActive(bool isActive)
        {
            _isSimulatorActive = isActive;

            if (_simulator == null)
                _simulator = SceneManager.GetActiveScene().GetComponentInChildren<XRInteractionSimulator>(true);

            if (_simulator == null)
            {
                if (isActive)
                    Logger.LogWarning("XRI backend: no XR Interaction Simulator in the active scene. " +
                                      "Add 'VR - XR Interaction Simulator.prefab' (inactive) to the player scene.",
                        LogCat.VR);
                ApplyKeyboardShortcuts(false);
                return;
            }

            if (isActive)
                AssignSimulatorControllers();

            _simulator.gameObject.SetActive(isActive);
            ApplyKeyboardShortcuts(isActive);
        }

        /// <summary>
        /// The simulator finds the controllers only via an XRInputModalityManager, which our rig doesn't have. Without
        /// them, point-and-click (mouse aims the controller ray) would aim the camera instead.
        /// Called before the simulator's GameObject is enabled, as it reads them in OnEnable.
        /// </summary>
        private void AssignSimulatorControllers()
        {
            var rig = Object.FindFirstObjectByType<XriRig>();
            if (rig == null)
                return;

            _simulatorRig = rig;

            if (_simulator.leftControllerTransform == null)
                _simulator.leftControllerTransform = rig.LeftHand;
            if (_simulator.rightControllerTransform == null)
                _simulator.rightControllerTransform = rig.RightHand;
        }

        /// <summary>
        /// The keyboard shortcuts of the "Gothic" map (Esc, Space, X, F) only work with the simulator, like on HVR.
        /// </summary>
        private void ApplyKeyboardShortcuts(bool isEnabled)
        {
            if (_gothicMap == null)
                return;

            _gothicMap.bindingMask = isEnabled ? null : InputBinding.MaskByGroup(_xrBindingGroup);
        }

        private static VrButtonState ToState(InputAction action)
        {
            if (action == null)
                return default;

            return new VrButtonState(action.IsPressed(), action.WasPressedThisFrame(), action.WasReleasedThisFrame());
        }
    }
}
