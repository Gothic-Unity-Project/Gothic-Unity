using System.Collections.Generic;

namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// The definitions a fact can complete, by fact kind and key. A fact checks only this small subset.
    /// Pure variable definitions have no fact: AchievementVariableWatchDomain checks VariableDefinitions after the
    /// script moments that change story variables.
    /// </summary>
    public class AchievementTriggerIndex
    {
        private static readonly List<AchievementDefinition> _none = new();

        private readonly Dictionary<(AchievementFactKind, int), List<AchievementDefinition>> _byInt = new();
        private readonly Dictionary<(AchievementFactKind, string), List<AchievementDefinition>> _byString = new();
        private readonly Dictionary<AchievementFactKind, List<AchievementDefinition>> _byKind = new();

        public readonly List<AchievementDefinition> VariableDefinitions = new();


        public void Add(AchievementFactKind kind, int key, AchievementDefinition definition)
        {
            AddOnce(_byInt, (kind, key), definition);
        }

        public void Add(AchievementFactKind kind, string key, AchievementDefinition definition)
        {
            AddOnce(_byString, (kind, key), definition);
        }

        /// <summary>
        /// Every fact of this kind (e.g. any true guild change).
        /// </summary>
        public void Add(AchievementFactKind kind, AchievementDefinition definition)
        {
            AddOnce(_byKind, kind, definition);
        }

        public List<AchievementDefinition> Get(in AchievementFact fact)
        {
            if (fact.StringKey != null)
                return _byString.TryGetValue((fact.Kind, fact.StringKey), out var byString) ? byString : _none;

            if (_byInt.TryGetValue((fact.Kind, fact.IntKey), out var byInt))
                return byInt;

            return _byKind.TryGetValue(fact.Kind, out var byKind) ? byKind : _none;
        }

        private static void AddOnce<TKey>(Dictionary<TKey, List<AchievementDefinition>> index, TKey key,
            AchievementDefinition definition)
        {
            if (!index.TryGetValue(key, out var definitions))
            {
                definitions = new List<AchievementDefinition>();
                index[key] = definitions;
            }

            if (!definitions.Contains(definition))
                definitions.Add(definition);
        }
    }
}
