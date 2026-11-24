namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// An item instance (drawn like the item itself) and/or a texture of the installation. The item wins; the texture
    /// is its fallback. Both empty, or neither found: placeholder.
    /// </summary>
    public class AchievementIcon
    {
        public string Texture;
        public string Item;
    }
}
