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
    }
}
