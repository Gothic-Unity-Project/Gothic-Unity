#if GUZ_HVR_INSTALLED
using System;
using System.Collections.Generic;
using System.Linq;

namespace GUZ.VR.Adapters.SpeechToText
{
    public class TextMatcher
    {
        private string[] referenceSentences;
    
        public TextMatcher(string[] sentences)
        {
            referenceSentences = sentences;
        }
        
        public MatchResult FindBestMatch(string input)
        {
            var results = new List<MatchResult>();
        
            for (var i = 0; i < referenceSentences.Length; i++)
            {
                var score = GetSimilarityScore(input, referenceSentences[i]);
                results.Add(new MatchResult
                {
                    Index = i,
                    Sentence = referenceSentences[i],
                    Score = score
                });
            }
        
            return results.OrderByDescending(r => r.Score).FirstOrDefault();
        }
    
        private float GetSimilarityScore(string input, string target)
        {
            // Normalize text
            var normalizedInput = NormalizeText(input);
            var normalizedTarget = NormalizeText(target);
        
            // Use Levenshtein distance
            var distance = LevenshteinDistance(normalizedInput, normalizedTarget);
            var maxLength = Math.Max(normalizedInput.Length, normalizedTarget.Length);
        
            return maxLength == 0 ? 1.0f : 1.0f - (float)distance / maxLength;
        }
    
        public static int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
            if (string.IsNullOrEmpty(target)) return source.Length;

            int[,] distance = new int[source.Length + 1, target.Length + 1];

            for (var i = 0; i <= source.Length; i++)
                distance[i, 0] = i;
            for (var j = 0; j <= target.Length; j++)
                distance[0, j] = j;

            for (var i = 1; i <= source.Length; i++)
            {
                for (var j = 1; j <= target.Length; j++)
                {
                    var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    distance[i, j] = Math.Min(Math.Min(
                            distance[i - 1, j] + 1,      // deletion
                            distance[i, j - 1] + 1),     // insertion
                        distance[i - 1, j - 1] + cost); // substitution
                }
            }

            return distance[source.Length, target.Length];
        }
        
        private string NormalizeText(string text)
        {
            return text.ToLower()
                .Replace(" ", string.Empty) // Remove spaces
                .Replace(",", string.Empty) // Remove punctuation
                .Replace(".", string.Empty)
                .Replace("'", string.Empty)
                .Replace("!", string.Empty)
                .Replace("?", string.Empty);
        }
    }

    public class MatchResult
    {
        public int Index { get; set; }
        public string Sentence { get; set; }
        public float Score { get; set; } // 0-1, where 1 is perfect match
    }
}
#endif
