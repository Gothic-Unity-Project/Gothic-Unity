namespace Gothic.VR.Models.FavoritesRing
{
    public static class FavoritesPutInActionExtension
    {
        /// <summary>
        /// The action as the log words it: "[FavoritesRing] Itfo_Apple a favorite".
        /// </summary>
        public static string ToLogText(this FavoritesPutInAction action)
        {
            return action switch
            {
                FavoritesPutInAction.Equipped => "equipped",
                FavoritesPutInAction.Favorite => "a favorite",
                FavoritesPutInAction.BackIntoBackpack => "back into the backpack",
                _ => "nothing"
            };
        }
    }
}
