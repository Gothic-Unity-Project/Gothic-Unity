#if GOTHIC_HVR_INSTALLED
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Shared;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    public class HvrHaptics : IVrHaptics
    {
        // Indexed by VrHapticPreset: amplitude, duration, frequency.
        private readonly HapticData[] _presets =
        {
            // Info
            new (0.15f, 0.25f, 30f),

            // Warning
            new (0.25f, 0.5f, 100f),

            // Error
            new (0.4f, 0.8f, 175f),

            // Success
            new (0.5f, 0.6f, 80f)
        };

        public void Vibrate(VrHandSide side, VrHapticPreset preset)
        {
            HVRInputManager.Instance.GetController(side.ToHvr()).Vibrate(_presets[(int)preset]);
        }
    }
}
#endif
