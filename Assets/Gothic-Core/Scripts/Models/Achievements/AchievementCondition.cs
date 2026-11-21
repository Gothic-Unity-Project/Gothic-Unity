using System.Collections.Generic;

namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// One condition of a definition (Docs/achievements.md, "Definition format"). Names come from the JSON, the
    /// Resolved* values from the scripts when the definitions load (AchievementDefinitionDomain).
    ///
    /// JSON per kind:
    /// LogTopic  { "logTopic": "CH1_JoinOC", "status": "success" }
    /// Variable  { "variable": "Kapitel", "op": ">=", "value": 3 } or "value": "LOG_SUCCESS"
    /// TrueGuild { "trueGuild": ["GIL_SLD"] }
    /// InfoTold  { "infoTold": "DIA_Gilbert_First" }
    /// Item      { "item": "ItMiNugget", "count": 15000 }
    /// Killed    { "killed": ["MinecrawlerQueen"], "count": 1, "byHero": true }
    /// NpcAivar  { "npcAivar": "GRD_251_Kirgo", "aivar": "AIV_WASDEFEATEDBYSC", "value": 1 }
    /// Counter   { "counter": "lockpick.success", "count": 20 }
    /// Distinct  { "distinct": "document.shown", "count": 10 }
    /// All/Any   { "all": [ ... ] } / { "any": [ ... ] }
    /// </summary>
    public class AchievementCondition
    {
        public AchievementConditionKind Kind;

        /// <summary>
        /// Script symbol (log topic constant, variable, info, item, NPC instance) or counter id.
        /// </summary>
        public string Name;

        /// <summary>
        /// TrueGuild: guild constants. Killed: NPC instances.
        /// </summary>
        public readonly List<string> Names = new();

        /// <summary>
        /// Variable/NpcAivar: an int literal, or ValueName if the JSON names a script constant.
        /// LogTopic: the status (ZenKit's SaveTopicStatus value).
        /// </summary>
        public int Value;

        public string ValueName;
        public AchievementCompareOp Op = AchievementCompareOp.Equal;

        /// <summary>
        /// Item/Killed/Counter/Distinct: how many are needed (at least 1).
        /// </summary>
        public int Count = 1;

        public bool IsByHero;

        /// <summary>
        /// NpcAivar: the aivar constant (or index as text).
        /// </summary>
        public string AivarName;

        /// <summary>
        /// All/Any: the combined conditions.
        /// </summary>
        public readonly List<AchievementCondition> Children = new();

        /// <summary>
        /// Killed: the run counter its kills go to ("kill:{definition id}:{n}"), set when the definitions load.
        /// </summary>
        public string KillCounterKey;

        // Resolved from the scripts.
        public int ResolvedIndex = -1;
        public int ResolvedAivarIndex = -1;
        public string ResolvedText;
        public readonly List<int> ResolvedIndices = new();


        public bool IsLeaf => Kind is not (AchievementConditionKind.All or AchievementConditionKind.Any);

        /// <summary>
        /// Only variables (also inside All/Any): the variable check after script moments checks it, no fact does.
        /// </summary>
        public bool IsPureVariable
        {
            get
            {
                if (IsLeaf)
                    return Kind == AchievementConditionKind.Variable;

                if (Children.Count == 0)
                    return false;

                foreach (var child in Children)
                {
                    if (!child.IsPureVariable)
                        return false;
                }

                return true;
            }
        }

        public static bool Compare(int left, AchievementCompareOp op, int right)
        {
            return op switch
            {
                AchievementCompareOp.Equal => left == right,
                AchievementCompareOp.NotEqual => left != right,
                AchievementCompareOp.Greater => left > right,
                AchievementCompareOp.GreaterOrEqual => left >= right,
                AchievementCompareOp.Less => left < right,
                AchievementCompareOp.LessOrEqual => left <= right,
                _ => false
            };
        }
    }
}
