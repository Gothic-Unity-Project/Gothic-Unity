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
        public Dictionary<string, AchievementRunRecord> Runs = new();


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
        /// Earned already in this tier or a better one, with an intact seal.
        /// </summary>
        public bool IsEarned(string id, AchievementTier tier)
        {
            var unlock = FindUnlock(id);
            return unlock != null && unlock.IsSealed && unlock.Tier >= tier;
        }

        /// <summary>
        /// Earned now in the run's tier: replaces an older unlock of it (a tier upgrade or an unsealed one).
        /// </summary>
        public void SetUnlock(string id, AchievementRunState run)
        {
            var previous = FindUnlock(id);
            if (previous != null)
                Unlocks.Remove(previous);

            Unlocks.Add(new AchievementUnlock
            {
                Id = id,
                Tier = run.Tier,
                DateUtc = DateTime.UtcNow.ToString("O"),
                RunId = run.RunId,
                SaveNo = run.SaveNo,
                IsSealed = true
            });
        }
    }
}
