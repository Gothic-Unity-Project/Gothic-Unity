namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// A hand/controller of the VR player.
    /// HINT: The lock picking events (GlobalEventDispatcher.LockPickCombo*) pass these values as int.
    /// They differ from GlobalEventDispatcher.HandSide (None=0, Left=1, Right=2, Both=3). Use
    /// VrHandSideExtensions to map between both.
    /// </summary>
    public enum VrHandSide
    {
        Left = 0,
        Right = 1
    }
}
