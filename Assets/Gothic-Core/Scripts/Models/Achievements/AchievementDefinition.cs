namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// One achievement of the definition file (StreamingAssets/Achievements/g1.json, g2.json or a mod's file).
    /// </summary>
    public class AchievementDefinition
    {
        public string Id;
        public readonly AchievementText Title = new();
        public readonly AchievementText Description = new();
        public readonly AchievementIcon Icon = new();
        public AchievementCondition Condition;

        /// <summary>
        /// A script symbol it needs doesn't exist (mod, renamed script). It's listed, but can't be earned.
        /// </summary>
        public bool IsUnavailable;

        /// <summary>
        /// The counter its progress is shown with (Counter, Distinct, Killed or Item with a count above 1), else null.
        /// </summary>
        public AchievementCondition ProgressCondition;
    }
}
