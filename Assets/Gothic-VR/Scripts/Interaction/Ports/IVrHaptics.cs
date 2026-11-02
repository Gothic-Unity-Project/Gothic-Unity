using Gothic.VR.Interaction.Models;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// Controller vibration. Which game event uses which preset is decided by VrHapticsService.
    /// </summary>
    public interface IVrHaptics
    {
        void Vibrate(VrHandSide side, VrHapticPreset preset);

        /// <summary>
        /// A vibration of its own strength (0-1) and length in seconds, e.g. a fist swing, a bow string, the
        /// favorites ring's ticks.
        /// </summary>
        void Vibrate(VrHandSide side, float amplitude, float duration);

        /// <summary>
        /// The preset with its amplitude scaled (0-1), e.g. contact haptics by impact speed. Backends without
        /// support play the preset unscaled.
        /// </summary>
        void Vibrate(VrHandSide side, VrHapticPreset preset, float amplitudeScale)
        {
            Vibrate(side, preset);
        }
    }
}
