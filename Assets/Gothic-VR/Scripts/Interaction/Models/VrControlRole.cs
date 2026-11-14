namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// What a stick or face button does in the current VrControlLayout. Grip and trigger have no role: they are the
    /// hands themselves (grab, use) and mean the same on both controllers.
    /// </summary>
    public enum VrControlRole
    {
        None,
        Move,
        Sprint,
        Menu,
        Turn,
        Jump,
        Crouch
    }
}
