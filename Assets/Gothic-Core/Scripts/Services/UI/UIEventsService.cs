using GUZ.Core.Logging;
using MyBox;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Services.UI
{
    public class UIEventsService
    {
        [Inject] private readonly FontService _fontService;


        public void SetHighlightFont(TMP_Text textComp)
        {
            if (textComp == null)
            {
                return;
            }

            // Font has default value assigned and somehow Unity marks spriteAsset as null in this case.
            if (textComp.spriteAsset == null)
            {
                textComp.spriteAsset = _fontService.HighlightSpriteAsset;
            }
            else
            {
                if (textComp.spriteAsset.name.EndsWith("_hi.fnt"))
                {
                    return;
                }

                SetFont(textComp, textComp.spriteAsset.name.RemoveEnd(".fnt") + "_hi");
            }
        }

        /// <summary>
        /// On certain conditions (like SetEnabled(false), the appropriate OnPointerExit() won't be recognized.
        /// Let's do it manually.
        /// </summary>
        public void SetDefaultFontsForChildren(GameObject root)
        {
            foreach (var textComp in root.GetComponentsInChildren<TMP_Text>())
            {
                SetDefaultFont(textComp);
            }
        }

        public void SetDefaultFont(TMP_Text textComp)
        {
            if (textComp == null)
            {
                return;
            }

            // Font has default value assigned and somehow Unity marks spriteAsset as null in this case.
            if (textComp.spriteAsset == null)
            {
                textComp.spriteAsset = _fontService.DefaultSpriteAsset;
            }
            else
            {
                SetFont(textComp, textComp.spriteAsset.name.RemoveEnd("_hi.fnt"));
            }
        }

        private  void SetFont(TMP_Text textComp, string fontName)
        {
            var newFont = _fontService.TryGetFont(fontName);

            if (newFont == null)
            {
                Logger.LogWarning($"Font {newFont} not found.", LogCat.Ui);
                return;
            }

            textComp.spriteAsset = newFont;
        }
    }
}
