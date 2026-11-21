namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// The game state conditions read at the moment of a fact (scripts, quest log, inventory). By symbol index, so
    /// AchievementConditionDomain works without ZenKit (EditMode tests use a fake).
    /// </summary>
    public interface IAchievementStateReader
    {
        /// <summary>
        /// An int script variable.
        /// </summary>
        int GetVariable(int symbolIndex);

        /// <summary>
        /// The status of a quest log topic (by its text), -1 if the topic doesn't exist.
        /// </summary>
        int GetLogTopicStatus(string topic);

        bool IsInfoTold(int infoIndex);

        int GetHeroTrueGuild();

        int GetHeroItemCount(int itemIndex);

        /// <summary>
        /// An aivar of the NPC with this instance. False if no such NPC is in the world.
        /// </summary>
        bool TryGetNpcAivar(int npcInstanceIndex, int aivarIndex, out int value);
    }
}
