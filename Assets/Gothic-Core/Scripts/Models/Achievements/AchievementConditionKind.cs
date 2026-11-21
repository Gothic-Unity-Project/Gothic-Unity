namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// The kinds of AchievementCondition. JSON names are in AchievementCondition's comments and Docs/achievements.md.
    /// </summary>
    public enum AchievementConditionKind
    {
        LogTopic,
        Variable,
        TrueGuild,
        InfoTold,
        Item,
        Killed,
        NpcAivar,
        Counter,
        Distinct,
        All,
        Any
    }
}
