namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// What happened in the game. AchievementService checks only the definitions indexed for a fact's kind and key.
    /// </summary>
    public enum AchievementFactKind
    {
        /// <summary>Log_SetTopicStatus. Key = topic text, Value = status.</summary>
        LogTopic,

        /// <summary>Npc_SetTrueGuild on the hero. Value = guild.</summary>
        TrueGuild,

        /// <summary>A dialog info was told. Key = info symbol index.</summary>
        InfoTold,

        /// <summary>The hero's items changed. Key = item symbol index.</summary>
        HeroItem,

        /// <summary>An NPC was killed. Key = the victim's instance symbol index.</summary>
        Kill,

        /// <summary>A counter or distinct counter rose. Key = counter id.</summary>
        Counter,

        /// <summary>A script moment that may have changed story variables: pure variable conditions are re-checked.
        /// </summary>
        VariableCheck
    }
}
