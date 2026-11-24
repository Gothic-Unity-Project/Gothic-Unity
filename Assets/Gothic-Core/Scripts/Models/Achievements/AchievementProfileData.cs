using System;
using System.Collections.Generic;

namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// The player's achievements of one game (and mod): &lt;persistentDataPath&gt;/Achievements/&lt;key&gt;.json.
    /// Independent of save games.
    /// </summary>
    public class AchievementProfileData
    {
        public List<AchievementUnlock> Unlocks = new();


        public AchievementUnlock FindUnlock(string id)
        {
            foreach (var unlock in Unlocks)
            {
                if (unlock.Id == id)
                    return unlock;
            }

            return null;
        }

        /// <summary>
        /// Earned already in this tier or a better one.
        /// </summary>
        public bool IsEarned(string id, AchievementTier tier)
        {
            var unlock = FindUnlock(id);
            return unlock != null && unlock.Tier >= tier;
        }

        /// <summary>
        /// Earned now in this tier: replaces an older unlock of a worse tier (a tier upgrade).
        /// </summary>
        public void SetUnlock(string id, AchievementTier tier)
        {
            var previous = FindUnlock(id);
            if (previous != null)
                Unlocks.Remove(previous);

            Unlocks.Add(new AchievementUnlock
            {
                Id = id,
                Tier = tier,
                DateUtc = DateTime.UtcNow.ToString("O")
            });
        }
    }
}
