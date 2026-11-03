namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// Vibration patterns. The VR backend defines amplitude, duration and frequency of each.
    /// </summary>
    public enum VrHapticPreset
    {
        /// <summary>
        /// e.g., you are inside an interaction collider
        /// </summary>
        Info,

        /// <summary>
        /// e.g., your lock pick rotation is invalid
        /// </summary>
        Warning,

        /// <summary>
        /// e.g., your key is broken
        /// </summary>
        Error,

        /// <summary>
        /// e.g., you opened a chest
        /// </summary>
        Success,

        /// <summary>
        /// e.g., a physical hand or the item it holds hit a wall. Short tick, amplitude scaled by impact speed.
        /// </summary>
        Contact,

        /// <summary>
        /// e.g., a hand reached a body slot it can use (backpack on the shoulder, hip holster). Short soft tick.
        /// </summary>
        Touch
    }
}
