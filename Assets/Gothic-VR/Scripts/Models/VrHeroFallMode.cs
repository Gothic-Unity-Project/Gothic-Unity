namespace Gothic.VR.Models
{
    /// <summary>
    /// How the view goes down when the game, not the player, takes the hero's body down: knocked out, dead, put to
    /// sleep. Same choices as VrLedgeClimbMode.
    /// HINT: Stored as int in Gothic.ini (VRConstants.IniNames.HeroFallMode). Never change the values.
    /// </summary>
    public enum VrHeroFallMode
    {
        /// <summary>
        /// The camera goes down and up with the VR hero body (VrHeroDownPose).
        /// </summary>
        MoveWithBody = 0,

        /// <summary>
        /// The view fades out, comes back at ground level and fades out and in again when he gets up (VrKnockoutView).
        /// </summary>
        Blink = 1
    }
}
