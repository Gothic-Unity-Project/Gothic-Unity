namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// What tells the player that a lip near a hand can be climbed.
    /// HINT: Stored as int in Gothic.ini (VRConstants.IniNames.LedgeHints). Never change the values.
    /// </summary>
    public enum VrLedgeHint
    {
        /// <summary>
        /// Nothing. Lips can still be grabbed.
        /// </summary>
        Off = 0,

        /// <summary>
        /// The hand half closes and ticks once.
        /// </summary>
        Touch = 1,

        /// <summary>
        /// Touch and a short glowing strip on the lip under the hand.
        /// </summary>
        Near = 2,

        /// <summary>
        /// Near and a bigger grab area.
        /// </summary>
        Assist = 3
    }
}
