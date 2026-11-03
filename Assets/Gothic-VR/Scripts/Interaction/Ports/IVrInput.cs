using Gothic.VR.Interaction.Models;
using UnityEngine;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// Raw controller input of the VR player. Gothic-specific inputs (menu, both grips) are derived from it in
    /// VRPlayer.
    /// </summary>
    public interface IVrInput
    {
        /// <summary>
        /// The player is controlled by keyboard and mouse instead of controllers.
        /// </summary>
        bool IsSimulator { get; }

        /// <summary>
        /// In the simulator, VRPlayer.IsBothGripsActive is the T key (HVR simulator: no way to hold both grips).
        /// The Xri simulator toggles each grip itself (Q+G, E+G), so both grips are just both toggled on.
        /// </summary>
        bool UsesBothGripsSimulatorKey { get; }

        VrButtonState GetButton(VrHandSide side, VrButton button);

        /// <summary>
        /// Analog grip value (0 = open, 1 = fully pressed). Drives the block hands' finger curl.
        /// Default: the grip button's state, for backends without an analog value.
        /// </summary>
        float GetGripValue(VrHandSide side)
        {
            return GetButton(side, VrButton.Grip).IsActive ? 1f : 0f;
        }

        /// <summary>
        /// The thumb rests on the primary or secondary button or on the thumbstick. Drives the block hands' thumb.
        /// Default: one of these buttons is pressed, for backends without touch input.
        /// </summary>
        bool IsThumbTouching(VrHandSide side)
        {
            return GetButton(side, VrButton.Primary).IsActive || GetButton(side, VrButton.Secondary).IsActive ||
                   GetButton(side, VrButton.JoystickButton).IsActive;
        }

        /// <summary>
        /// Velocity of the tracked controller, relative to IVrRig.TrackingSpace.
        /// </summary>
        Vector3 GetControllerVelocity(VrHandSide side);

        /// <summary>
        /// Activates the desktop device simulator (DeveloperConfig.EnableVRDeviceSimulator).
        /// </summary>
        void SetDeviceSimulatorActive(bool isActive);
    }
}
