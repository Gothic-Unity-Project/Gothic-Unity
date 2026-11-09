namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// An item behaviour that only runs while a hand holds the item (VRBow, VRCrossbow). VRVobItem adds it on the
    /// first grab (VRRangedService.AddRangedWeapon) and only switches it - it doesn't know the concrete kinds.
    /// </summary>
    public interface IVrHoldable
    {
        /// <summary>
        /// A hand took the item (true) or the last hand let it go (false). A second hand changes nothing. Called
        /// inside the backend's grab events: heavy one-time setup has to wait for the component's next Update.
        /// </summary>
        void SetHeld(bool isHeld);
    }
}
