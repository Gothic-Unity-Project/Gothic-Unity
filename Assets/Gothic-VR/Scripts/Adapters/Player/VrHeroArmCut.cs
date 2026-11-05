namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Which part of a body mesh VrHeroBodyMeshCutter.CutArms keeps.
    /// </summary>
    public enum VrHeroArmCut
    {
        // The whole body without the model's hands (the VR hands are shown).
        WithoutHands,
        // Only the forearms (naked body: rolled-up sleeves).
        Forearms,
        // The forearms and the hands (rolled-up sleeves with the hero's own hands, EnableHeroHands).
        ForearmsAndHands,
        // Everything but forearms and hands (armor with rolled-up sleeves).
        Sleeveless
    }
}
