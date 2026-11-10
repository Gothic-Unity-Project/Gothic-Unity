using ZenKit.Daedalus;

namespace Gothic.VR.Models.FavoritesRing
{
    /// <summary>
    /// What one slot of the favorites ring holds (VrFavoritesService.BuildSlots). The ring shows it as a
    /// FavoritesRingSlotView.
    /// </summary>
    public class FavoriteSlot
    {
        public FavoriteSlotKind Kind;
        // null = the empty slot to put into
        public ItemInstance Item;
        public string InstanceName;
        // How many are in the backpack (not in a hand or holster).
        public int Amount;

        public bool IsWeapon => Kind is FavoriteSlotKind.Melee or FavoriteSlotKind.Ranged;
        public bool IsReal => IsWeapon || Kind is FavoriteSlotKind.Item or FavoriteSlotKind.Currency;
    }
}
