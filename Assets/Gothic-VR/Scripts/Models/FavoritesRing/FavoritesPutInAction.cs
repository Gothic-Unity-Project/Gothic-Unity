namespace Gothic.VR.Models.FavoritesRing
{
    /// <summary>
    /// What letting go of a held item at a ring slot does (VrFavoritesPutInDomain.TryPutIn).
    /// </summary>
    public enum FavoritesPutInAction
    {
        // Can't go in there.
        None,
        // A rune/scroll or weapon becomes the equipped one.
        Equipped,
        // Any other item becomes a favorite.
        Favorite,
        // Its own slot, or one it shows in already: it just goes back.
        BackIntoBackpack
    }
}
