namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// Gothic actions of the "Gothic" map in GothicVR.inputactions. Read by the XRI rig (locomotion), not by game
    /// code, which uses IVrInput.GetButton().
    /// </summary>
    public enum XriGothicAction
    {
        Menu,
        Jump,
        Crouch,
        Sprint
    }
}
