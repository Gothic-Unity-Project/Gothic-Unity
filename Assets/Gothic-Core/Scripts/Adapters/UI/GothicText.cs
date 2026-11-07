using Gothic.Core.Const;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.UI;
using TMPro;

namespace Gothic.Core.Adapters.UI
{
    /// <summary>
    /// Texts created at runtime in Gothic's font: an empty TMP font, so every character falls back to the sprite asset
    /// made from the game's font (FontService). Only works after the game booted (FontService created the sprites).
    /// </summary>
    public static class GothicText
    {
        /// <param name="isTinted">The font sprites take the text color (e.g. red/green numbers).</param>
        public static void Apply(TMP_Text text, bool isHighlighted = false, bool isTinted = false)
        {
            var emptyFont = ReflexProjectInstaller.DIContainer.Resolve<ResourceCacheService>()
                .TryGetFontAsset(Constants.Fonts.GothicEmptyPath);
            if (emptyFont != null)
                text.font = emptyFont;

            var fontService = ReflexProjectInstaller.DIContainer.Resolve<FontService>();
            var spriteAsset = isHighlighted ? fontService.HighlightSpriteAsset : fontService.DefaultSpriteAsset;
            if (spriteAsset != null)
                text.spriteAsset = spriteAsset;

            text.tintAllSprites = isTinted;
        }
    }
}
