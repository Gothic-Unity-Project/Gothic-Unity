using Gothic.Core.Extensions;
using Gothic.Core.Services.Caches;
using UnityEngine;
using UnityEngine.UI;

namespace Gothic.Core.Adapters.UI.Achievements
{
    /// <summary>
    /// One rule for an achievement's icon, shared by the toast and the page line: the item's picture first, then the
    /// texture, else the placeholder. Item pictures are drawn upright, Gothic textures need the flip.
    /// </summary>
    public static class AchievementIconView
    {
        /// <returns>False: a texture was named but isn't in the installation (the caller logs it).</returns>
        public static bool Show(RawImage image, GameObject placeholder, string item, string texture,
            ItemIconService itemIconService, TextureCacheService textureCacheService)
        {
            Texture icon = itemIconService.TryGetIcon(item);
            var isGothicTexture = icon == null && !string.IsNullOrEmpty(texture);
            if (isGothicTexture)
                icon = textureCacheService.TryGetTexture(texture);

            image.texture = icon;
            image.SetUpright(isGothicTexture);
            image.gameObject.SetActive(icon != null);
            placeholder.SetActive(icon == null);
            return !isGothicTexture || icon != null;
        }
    }
}
