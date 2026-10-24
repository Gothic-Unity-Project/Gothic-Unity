namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// How the camera height is calculated.
    /// HINT: Stored as int in Gothic.ini (VRConstants.IniNames.SitStand). Never change the values.
    /// </summary>
    public enum VrSitStand
    {
        Sitting = 0,
        Standing = 1,
        PlayerHeight = 2
    }
}
