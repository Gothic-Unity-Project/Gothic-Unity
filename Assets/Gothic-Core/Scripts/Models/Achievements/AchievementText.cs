using System.Collections.Generic;

namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// A text in every language the definition file brings ("en", "de", ...). Missing language: English.
    /// </summary>
    public class AchievementText
    {
        private const string _fallbackLanguage = "en";

        public readonly Dictionary<string, string> Translations = new();


        public string Get(string language)
        {
            if (!string.IsNullOrEmpty(language) && Translations.TryGetValue(language.ToLowerInvariant(), out var text))
                return text;

            return Translations.TryGetValue(_fallbackLanguage, out var fallback) ? fallback : string.Empty;
        }
    }
}
