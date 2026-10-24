namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// State of a controller button in the current frame.
    /// </summary>
    public readonly struct VrButtonState
    {
        /// <summary>
        /// The button is held down.
        /// </summary>
        public readonly bool IsActive;

        /// <summary>
        /// The button went down this frame.
        /// </summary>
        public readonly bool IsJustActivated;

        /// <summary>
        /// The button went up this frame.
        /// </summary>
        public readonly bool IsJustDeactivated;

        public VrButtonState(bool isActive, bool isJustActivated, bool isJustDeactivated)
        {
            IsActive = isActive;
            IsJustActivated = isJustActivated;
            IsJustDeactivated = isJustDeactivated;
        }
    }
}
