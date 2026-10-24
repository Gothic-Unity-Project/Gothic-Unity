namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// Which device defines "forward" for thumbstick movement.
    /// HINT: Stored as int in Gothic.ini (VRConstants.IniNames.MoveDirection). Never change the values.
    /// </summary>
    public enum VrMoveDirection
    {
        Camera = 0,
        LeftController = 1,
        RightController = 2
    }
}
