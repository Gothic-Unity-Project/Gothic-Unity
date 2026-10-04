using Gothic.Core;
using Gothic.Core.Services.UI;
using TMPro;
using UnityEngine;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// Texts created at runtime in Gothic's font like the dialogs: an empty TMP font, so every character falls back to
    /// the sprite asset made from the game's font (FontService). TMP's default font showed nothing.
    /// </summary>
    public static class VRGothicText
    {
        private const string _emptyFontPath = "FontAsset/LiberationSans SDF empty";

        private static TMP_FontAsset _emptyFont;

        public static void Apply(TMP_Text text, bool isHighlighted = false)
        {
            if (_emptyFont == null)
                _emptyFont = Resources.Load<TMP_FontAsset>(_emptyFontPath);
            if (_emptyFont != null)
                text.font = _emptyFont;

            var fontService = ReflexProjectInstaller.DIContainer.Resolve<FontService>();
            var spriteAsset = isHighlighted ? fontService.HighlightSpriteAsset : fontService.DefaultSpriteAsset;
            if (spriteAsset != null)
                text.spriteAsset = spriteAsset;
        }
    }
}
