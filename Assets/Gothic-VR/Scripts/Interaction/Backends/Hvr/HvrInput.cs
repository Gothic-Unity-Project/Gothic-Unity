#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Extensions;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Shared;
using HurricaneVRExtensions.Simulator;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    public class HvrInput : IVrInput
    {
        private HVRPlayerInputs _playerInputs;

        public bool IsSimulator
        {
            get
            {
                if (_playerInputs == null)
                    _playerInputs = Object.FindFirstObjectByType<HVRPlayerInputs>(FindObjectsInactive.Include);

                return _playerInputs != null && _playerInputs.UseWASD;
            }
        }

        public bool UsesBothGripsSimulatorKey => true;

        public VrButtonState GetButton(VrHandSide side, VrButton button)
        {
            return HVRController.GetButtonState(side.ToHvr(), button.ToHvr()).ToVr();
        }

        public Vector3 GetControllerVelocity(VrHandSide side)
        {
            return HVRInputManager.Instance.GetController(side.ToHvr()).Velocity;
        }

        public Vector2 GetThumbstick(VrHandSide side)
        {
            var controller = HVRInputManager.Instance != null
                ? HVRInputManager.Instance.GetController(side.ToHvr())
                : null;
            return controller != null ? controller.JoystickAxis : Vector2.zero;
        }

        public void SetDeviceSimulatorActive(bool isActive)
        {
            var simulator = SceneManager.GetActiveScene().GetComponentInChildren<HVRBodySimulator>(true)!;
            simulator.gameObject.SetActive(isActive);
        }
    }
}
#endif
