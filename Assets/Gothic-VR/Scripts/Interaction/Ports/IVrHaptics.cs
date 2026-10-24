using Gothic.VR.Interaction.Models;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// Controller vibration. Which game event uses which preset is decided by VrHapticsService.
    /// </summary>
    public interface IVrHaptics
    {
        void Vibrate(VrHandSide side, VrHapticPreset preset);
    }
}
