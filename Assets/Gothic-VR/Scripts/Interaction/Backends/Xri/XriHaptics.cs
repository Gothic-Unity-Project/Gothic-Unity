using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// Sends the haptic presets through the "Haptic" actions of GothicVR.inputactions (same mechanism as XRI's
    /// HapticImpulsePlayer, but without needing a component on the rig).
    /// </summary>
    public class XriHaptics : IVrHaptics
    {
        // Indexed by VrHapticPreset. Same values as the HVR backend sends to the device: HVR's
        // HapticData(duration, amplitude, frequency) constructor gets them in that order.
        private static readonly (float Amplitude, float Duration, float Frequency)[] _presets =
        {
            // Info
            (0.25f, 0.15f, 30f),

            // Warning
            (0.5f, 0.25f, 100f),

            // Error
            (0.8f, 0.4f, 175f),

            // Success
            (0.6f, 0.5f, 80f)
        };

        private readonly XriInput _input;
        private readonly XRInputHapticImpulseProvider[] _providers = new XRInputHapticImpulseProvider[2];
        private readonly bool[] _isMissingDeviceLogged = new bool[2];

        public XriHaptics(XriInput input)
        {
            _input = input;
        }

        public void Vibrate(VrHandSide side, VrHapticPreset preset)
        {
            var channel = GetProvider(side)?.GetChannelGroup()?.GetChannel();
            if (channel == null)
            {
                if (!_isMissingDeviceLogged[(int)side])
                {
                    Logger.LogWarning($"XRI backend: no haptic device for {side} hand. Vibration skipped.", LogCat.VR);
                    _isMissingDeviceLogged[(int)side] = true;
                }

                return;
            }

            var (amplitude, duration, frequency) = _presets[(int)preset];
            channel.SendHapticImpulse(amplitude, duration, frequency);
        }

        private XRInputHapticImpulseProvider GetProvider(VrHandSide side)
        {
            if (_providers[(int)side] != null)
                return _providers[(int)side];

            var action = _input.GetHapticAction(side);
            if (action == null)
                return null;

            _providers[(int)side] = new XRInputHapticImpulseProvider
            {
                inputSourceMode = XRInputHapticImpulseProvider.InputSourceMode.InputActionReference,
                inputActionReference = InputActionReference.Create(action)
            };

            return _providers[(int)side];
        }
    }
}
