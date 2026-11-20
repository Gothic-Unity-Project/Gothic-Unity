namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// A texture of the installation (works today) or an item instance (needs an item renderer, falls back to the
    /// texture or a placeholder until then). Both empty: placeholder.
    /// </summary>
    public class AchievementIcon
    {
        public string Texture;
        public string Item;
    }
}
