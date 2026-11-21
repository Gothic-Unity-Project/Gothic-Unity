using System.Collections.Generic;
using Gothic.Core.Models.Achievements;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// Pure variable conditions (Docs/achievements.md, "How it works"). Script moments mark the variables dirty, one
    /// check follows a frame later however many moments came. Only a value that differs from the snapshot can unlock:
    /// the snapshot is taken when a new game or a save is ready, a level change keeps it. Pure: the state comes from
    /// an IAchievementStateReader (EditMode-testable).
    /// </summary>
    public class AchievementVariableWatchDomain
    {
        private static readonly AchievementFact _variableCheck = new(AchievementFactKind.VariableCheck);

        private readonly AchievementConditionDomain _conditionDomain = new();
        private readonly Dictionary<int, int> _snapshot = new();

        /// <summary>
        /// A script moment came since the last check.
        /// </summary>
        public bool IsDirty { get; private set; }


        /// <summary>
        /// True only for the first moment since the last check: the caller schedules one check for the next frame.
        /// </summary>
        public bool MarkDirty()
        {
            if (IsDirty)
                return false;

            IsDirty = true;
            return true;
        }

        /// <summary>
        /// Forget a pending check (the world is left, nothing may count until it is ready).
        /// </summary>
        public void ClearDirty()
        {
            IsDirty = false;
        }

        /// <summary>
        /// The values of this moment are the base line (new game or loaded save ready): no retroactive unlocks.
        /// </summary>
        public void TakeSnapshot(List<AchievementDefinition> definitions, IAchievementStateReader reader)
        {
            _snapshot.Clear();
            IsDirty = false;
            foreach (var definition in definitions)
                Snapshot(definition.Condition, reader);
        }

        /// <summary>
        /// Adds to met every definition whose variables changed since the snapshot and whose condition is met now.
        /// Clears the dirty mark. Also without a mark (fallback checks: help panel, achievements page, save).
        /// </summary>
        public void Check(List<AchievementDefinition> definitions, IAchievementStateReader reader,
            AchievementRunState run, List<AchievementDefinition> met)
        {
            IsDirty = false;
            foreach (var definition in definitions)
            {
                if (HasChanged(definition.Condition, reader) &&
                    _conditionDomain.IsMet(definition.Condition, _variableCheck, reader, run))
                    met.Add(definition);
            }
        }

        private void Snapshot(AchievementCondition condition, IAchievementStateReader reader)
        {
            if (condition.Kind == AchievementConditionKind.Variable)
                _snapshot[condition.ResolvedIndex] = reader.GetVariable(condition.ResolvedIndex);

            foreach (var child in condition.Children)
                Snapshot(child, reader);
        }

        private bool HasChanged(AchievementCondition condition, IAchievementStateReader reader)
        {
            if (condition.Kind == AchievementConditionKind.Variable)
                return !_snapshot.TryGetValue(condition.ResolvedIndex, out var value) ||
                       reader.GetVariable(condition.ResolvedIndex) != value;

            foreach (var child in condition.Children)
            {
                if (HasChanged(child, reader))
                    return true;
            }

            return false;
        }
    }
}
