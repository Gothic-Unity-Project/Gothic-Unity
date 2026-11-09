using Gothic.VR.Interaction.Components;
using ZenKit.Daedalus;

namespace Gothic.VR.Models.FavoritesRing
{
    /// <summary>
    /// A weapon taken out of the favorites ring, until it was let go.
    /// </summary>
    public class FavoritesRingTakenWeapon
    {
        public VrInteractable Grabbable;
        public ItemInstance Item;
        public string InstanceName;
        public FavoriteSlotKind Kind;
        public float ReleasedTime = -1f;
    }
}
