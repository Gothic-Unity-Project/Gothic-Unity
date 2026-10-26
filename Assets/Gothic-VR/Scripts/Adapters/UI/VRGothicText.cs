using Gothic.Core.Adapters.UI;
using TMPro;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// Texts created at runtime in Gothic's font like the dialogs: an empty TMP font, so every character falls back to
    /// the sprite asset made from the game's font (FontService). TMP's default font showed nothing.
    /// </summary>
    public static class VRGothicText
    {
        public static void Apply(TMP_Text text, bool isHighlighted = false)
        {
            GothicText.Apply(text, isHighlighted);
        }
    }
}
