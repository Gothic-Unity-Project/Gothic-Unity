using System.Collections.Generic;

namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// The running game's achievement data: tier and counters. It goes into the save game (section
    /// core.achievements).
    /// </summary>
    public class AchievementRunState
    {
        public AchievementTier Tier;
        public Dictionary<string, int> Counters = new();
        public Dictionary<string, HashSet<string>> Distinct = new();


        public int GetCounter(string id)
        {
            return id != null && Counters.TryGetValue(id, out var value) ? value : 0;
        }

        public int Increment(string id)
        {
            var value = GetCounter(id) + 1;
            Counters[id] = value;
            return value;
        }

        public int GetDistinctCount(string id)
        {
            return id != null && Distinct.TryGetValue(id, out var keys) ? keys.Count : 0;
        }

        /// <summary>
        /// Returns false if the key was counted already.
        /// </summary>
        public bool AddDistinct(string id, string key)
        {
            if (!Distinct.TryGetValue(id, out var keys))
            {
                keys = new HashSet<string>();
                Distinct[id] = keys;
            }

            return keys.Add(key);
        }

        /// <summary>
        /// Marvin mode lowers it - never raises it.
        /// </summary>
        public void LowerTierTo(AchievementTier tier)
        {
            if (tier < Tier)
                Tier = tier;
        }
    }
}
