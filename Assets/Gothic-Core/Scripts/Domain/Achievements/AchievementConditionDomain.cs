using Gothic.Core.Models.Achievements;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// Checks one condition at the moment of a fact. Facts the state doesn't keep (the guild just set, a kill) come
    /// from the fact itself, everything else is read synchronously - "Hired Early" reads Kapitel inside the guild
    /// fact. Pure: no ZenKit, no Unity (EditMode-testable).
    /// </summary>
    public class AchievementConditionDomain
    {
        public bool IsMet(AchievementCondition condition, in AchievementFact fact, IAchievementStateReader reader,
            AchievementRunState run)
        {
            switch (condition.Kind)
            {
                case AchievementConditionKind.All:
                    foreach (var child in condition.Children)
                    {
                        if (!IsMet(child, fact, reader, run))
                            return false;
                    }
                    return condition.Children.Count > 0;
                case AchievementConditionKind.Any:
                    foreach (var child in condition.Children)
                    {
                        if (IsMet(child, fact, reader, run))
                            return true;
                    }
                    return false;
                case AchievementConditionKind.LogTopic:
                    var status = fact.Kind == AchievementFactKind.LogTopic && fact.StringKey == condition.ResolvedText
                        ? fact.Value
                        : reader.GetLogTopicStatus(condition.ResolvedText);
                    return status == condition.Value;
                case AchievementConditionKind.Variable:
                    return AchievementCondition.Compare(reader.GetVariable(condition.ResolvedIndex), condition.Op,
                        condition.Value);
                case AchievementConditionKind.TrueGuild:
                    var guild = fact.Kind == AchievementFactKind.TrueGuild ? fact.Value : reader.GetHeroTrueGuild();
                    return condition.ResolvedIndices.Contains(guild);
                case AchievementConditionKind.InfoTold:
                    return reader.IsInfoTold(condition.ResolvedIndex);
                case AchievementConditionKind.Item:
                    return reader.GetHeroItemCount(condition.ResolvedIndex) >= condition.Count;
                case AchievementConditionKind.Killed:
                    return run.GetCounter(condition.KillCounterKey) >= condition.Count;
                case AchievementConditionKind.NpcAivar:
                    return reader.TryGetNpcAivar(condition.ResolvedIndex, condition.ResolvedAivarIndex, out var aivar)
                           && AchievementCondition.Compare(aivar, condition.Op, condition.Value);
                case AchievementConditionKind.Counter:
                    return run.GetCounter(condition.Name) >= condition.Count;
                case AchievementConditionKind.Distinct:
                    return run.GetDistinctCount(condition.Name) >= condition.Count;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A kill fact raises the run counter of every Killed condition it matches (victim, by the hero if needed).
        /// Before IsMet().
        /// </summary>
        public void CountKill(AchievementCondition condition, in AchievementFact fact, AchievementRunState run)
        {
            if (!condition.IsLeaf)
            {
                foreach (var child in condition.Children)
                    CountKill(child, fact, run);
                return;
            }

            if (condition.Kind != AchievementConditionKind.Killed || fact.Kind != AchievementFactKind.Kill ||
                !condition.ResolvedIndices.Contains(fact.IntKey) || (condition.IsByHero && !fact.IsByHero))
                return;

            run.Increment(condition.KillCounterKey);
        }

        /// <summary>
        /// Current value of a counter-like condition for the progress bar.
        /// </summary>
        public int GetProgress(AchievementCondition condition, IAchievementStateReader reader, AchievementRunState run)
        {
            return condition.Kind switch
            {
                AchievementConditionKind.Counter => run.GetCounter(condition.Name),
                AchievementConditionKind.Distinct => run.GetDistinctCount(condition.Name),
                AchievementConditionKind.Killed => run.GetCounter(condition.KillCounterKey),
                AchievementConditionKind.Item => reader?.GetHeroItemCount(condition.ResolvedIndex) ?? 0,
                _ => 0
            };
        }
    }
}
