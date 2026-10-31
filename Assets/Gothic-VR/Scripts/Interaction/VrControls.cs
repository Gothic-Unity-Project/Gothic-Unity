using Gothic.Core;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Interaction
{
    /// <summary>
    /// Controller buttons, velocities and vibration of the active VR backend for components that aren't injected
    /// (items, spells and gestures created at runtime). Resolved on every call: the ports live in the VR scene's
    /// container, which is rebuilt with the scene.
    /// </summary>
    public static class VrControls
    {
        public static VrButtonState GetButton(VrHandSide side, VrButton button)
        {
            return ReflexProjectInstaller.DIContainer.Resolve<IVrInput>().GetButton(side, button);
        }

        /// <summary>
        /// Velocity of the tracked controller, relative to IVrRig.TrackingSpace.
        /// </summary>
        public static Vector3 GetVelocity(VrHandSide side)
        {
            return ReflexProjectInstaller.DIContainer.Resolve<IVrInput>().GetControllerVelocity(side);
        }

        public static Vector2 GetThumbstick(VrHandSide side)
        {
            return ReflexProjectInstaller.DIContainer.Resolve<IVrInput>().GetThumbstick(side);
        }

        public static void Vibrate(VrHandSide side, float amplitude, float duration)
        {
            ReflexProjectInstaller.DIContainer.Resolve<IVrHaptics>().Vibrate(side, amplitude, duration);
        }
    }
}
