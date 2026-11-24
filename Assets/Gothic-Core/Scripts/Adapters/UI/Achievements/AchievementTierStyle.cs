using Gothic.Core.Models.Achievements;
using UnityEngine;

namespace Gothic.Core.Adapters.UI.Achievements
{
    /// <summary>
    /// How a tier looks in the toast and on the page: the god's name in his colour. Adanos blue like water, Beliar
    /// dark red. Text and colour only, no art.
    /// </summary>
    public static class AchievementTierStyle
    {
        private static readonly Color _adanosColor = new(0.35f, 0.65f, 1f);
        private static readonly Color _beliarColor = new(0.85f, 0.18f, 0.15f);

        public static Color GetColor(AchievementTier tier)
        {
            return tier switch
            {
                AchievementTier.Adanos => _adanosColor,
                _ => _beliarColor
            };
        }

        /// <summary>
        /// The god's name - the same in every language.
        /// </summary>
        public static string GetName(AchievementTier tier)
        {
            return tier switch
            {
                AchievementTier.Adanos => "Adanos",
                _ => "Beliar"
            };
        }

        /// <summary>
        /// The name in the tier's colour (TMP rich text). Gothic's font sprites need tintAllSprites on the text.
        /// </summary>
        public static string GetColoredName(AchievementTier tier)
        {
            return $"<color=#{ColorUtility.ToHtmlStringRGB(GetColor(tier))}>{GetName(tier)}</color>";
        }
    }
}
