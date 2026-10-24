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
        Success
    }
}
