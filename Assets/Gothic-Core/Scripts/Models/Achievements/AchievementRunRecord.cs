namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// What the profile knows about a run: its newest committed save number and whether the hero died after it.
    /// </summary>
    public class AchievementRunRecord
    {
        public int NewestSaveNo;
        public bool IsSpent;
    }
}
