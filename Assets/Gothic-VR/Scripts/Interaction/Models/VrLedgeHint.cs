namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// What tells the player that a lip near a hand can be climbed. The hand always half closes and ticks once.
    /// HINT: Stored as int in Gothic.ini (VRConstants.IniNames.LedgeHints). Never change the values.
    /// </summary>
    public enum VrLedgeHint
    {
        /// <summary>
        /// Only the hand: it half closes and ticks once.
        /// </summary>
        Off = 0,

        /// <summary>
        /// The hand and a short glowing strip on the lip under the hand.
        /// </summary>
        Glow = 1
    }
}
