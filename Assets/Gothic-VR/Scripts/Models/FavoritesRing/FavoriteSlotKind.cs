namespace Gothic.VR.Models.FavoritesRing
{
    /// <summary>
    /// Which favorites ring slot an item belongs into (VrFavoritesService.GetKind).
    /// </summary>
    public enum FavoriteSlotKind
    {
        Empty,
        Spell,
        Melee,
        Ranged,
        Item,
        Currency
    }
}
