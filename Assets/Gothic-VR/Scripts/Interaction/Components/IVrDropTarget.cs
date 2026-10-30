namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Takes an item that a hand let go of inside its drop zone (chest content, over-the-shoulder stash).
    /// The VrDropZone adapter reports the contacts to VrDropZoneService, which calls this one frame after the release.
    /// </summary>
    public interface IVrDropTarget
    {
        /// <summary>
        /// The target would take the item right now.
        /// </summary>
        bool CanTake(VrInteractable item);

        /// <summary>
        /// Takes the item (it is not held any longer). False: nothing happened.
        /// </summary>
        bool TryTake(VrInteractable item);
    }
}
