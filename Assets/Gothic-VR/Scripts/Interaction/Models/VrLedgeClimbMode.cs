namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// How the player gets onto a ledge.
    /// HINT: Stored as int in Gothic.ini (VRConstants.IniNames.LedgeClimbMode). Never change the values.
    /// </summary>
    public enum VrLedgeClimbMode
    {
        /// <summary>
        /// Grab the lip and pull yourself up.
        /// </summary>
        Physical = 0,

        /// <summary>
        /// Grabbing the lip (or jumping in front of it) fades out, puts the player on top and fades in.
        /// </summary>
        Comfort = 1
    }
}
