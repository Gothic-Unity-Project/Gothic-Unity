namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// An earned achievement in the profile: in which tier and when.
    /// </summary>
    public class AchievementUnlock
    {
        public string Id;
        public AchievementTier Tier;
        public string DateUtc;
    }
}
