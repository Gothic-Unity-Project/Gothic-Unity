using System;
using System.Collections.Generic;
using Gothic.Core.Models.Achievements;
using Newtonsoft.Json.Linq;
using ZenKit;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// The definition file format (Docs/achievements.md, "Definition format") to AchievementDefinition, JSON only:
    /// no files, no scripts (symbols are resolved by AchievementDefinitionDomain). Mod authors: an error here names
    /// the definition it skipped and why.
    /// </summary>
    public static class AchievementDefinitionParser
    {
        /// <summary>
        /// Format: { "achievements": [ { "id", "title", "description", "icon", "hidden", "condition" } ] }.
        /// A definition that doesn't parse is skipped and named in errors. Ids must be unique.
        /// </summary>
        public static List<AchievementDefinition> Parse(string json, List<string> errors)
        {
            var definitions = new List<AchievementDefinition>();
            var ids = new HashSet<string>();

            JArray entries;
            try
            {
                entries = JObject.Parse(json)["achievements"] as JArray;
            }
            catch (Exception e)
            {
                errors.Add($"The file isn't valid JSON: {e.Message}");
                return definitions;
            }

            if (entries == null)
            {
                errors.Add("The file has no \"achievements\" list.");
                return definitions;
            }

            foreach (var entry in entries)
            {
                var id = entry.Value<string>("id");
                try
                {
                    var definition = ParseDefinition(entry as JObject);
                    if (!ids.Add(definition.Id))
                        throw new FormatException("the id is used twice");

                    definitions.Add(definition);
                }
                catch (Exception e)
                {
                    errors.Add($"{id ?? "(no id)"}: {e.Message}");
                }
            }

            return definitions;
        }

        private static AchievementDefinition ParseDefinition(JObject entry)
        {
            if (entry == null)
                throw new FormatException("an entry isn't an object");

            var definition = new AchievementDefinition
            {
                Id = entry.Value<string>("id"),
                IsHidden = entry.Value<bool?>("hidden") ?? false,
                Condition = ParseCondition(entry["condition"] as JObject)
            };

            if (string.IsNullOrEmpty(definition.Id))
                throw new FormatException("the id is missing");

            ParseText(entry["title"], definition.Title);
            ParseText(entry["description"], definition.Description);
            if (entry["icon"] is JObject icon)
            {
                definition.Icon.Texture = icon.Value<string>("texture");
                definition.Icon.Item = icon.Value<string>("item");
            }

            var killedCount = 0;
            AssignKillCounters(definition.Condition, definition.Id, ref killedCount);
            definition.ProgressCondition = FindProgressCondition(definition.Condition);
            return definition;
        }

        private static void ParseText(JToken token, AchievementText text)
        {
            if (token is not JObject translations)
                throw new FormatException("title or description is missing");

            foreach (var property in translations.Properties())
                text.Translations[property.Name.ToLowerInvariant()] = property.Value.Value<string>() ?? string.Empty;
        }

        public static AchievementCondition ParseCondition(JObject json)
        {
            if (json == null)
                throw new FormatException("the condition is missing");

            if (json["all"] is JArray all)
                return ParseGroup(AchievementConditionKind.All, all);
            if (json["any"] is JArray any)
                return ParseGroup(AchievementConditionKind.Any, any);

            var condition = new AchievementCondition
            {
                Count = Math.Max(1, json.Value<int?>("count") ?? 1),
                IsByHero = json.Value<bool?>("byHero") ?? false
            };

            if (json["logTopic"] != null)
            {
                condition.Kind = AchievementConditionKind.LogTopic;
                condition.Name = RequireString(json, "logTopic");
                condition.Value = ParseTopicStatus(json.Value<string>("status") ?? "success");
            }
            else if (json["variable"] != null)
            {
                condition.Kind = AchievementConditionKind.Variable;
                condition.Name = RequireString(json, "variable");
                condition.Op = ParseOp(json.Value<string>("op") ?? "==");
                ParseValue(json, condition);
            }
            else if (json["trueGuild"] != null)
            {
                condition.Kind = AchievementConditionKind.TrueGuild;
                ParseNames(json["trueGuild"], condition);
            }
            else if (json["infoTold"] != null)
            {
                condition.Kind = AchievementConditionKind.InfoTold;
                condition.Name = RequireString(json, "infoTold");
            }
            else if (json["item"] != null)
            {
                condition.Kind = AchievementConditionKind.Item;
                condition.Name = RequireString(json, "item");
            }
            else if (json["killed"] != null)
            {
                condition.Kind = AchievementConditionKind.Killed;
                ParseNames(json["killed"], condition);
            }
            else if (json["npcAivar"] != null)
            {
                condition.Kind = AchievementConditionKind.NpcAivar;
                condition.Name = RequireString(json, "npcAivar");
                condition.AivarName = RequireString(json, "aivar");
                condition.Op = ParseOp(json.Value<string>("op") ?? "==");
                ParseValue(json, condition);
            }
            else if (json["counter"] != null)
            {
                condition.Kind = AchievementConditionKind.Counter;
                condition.Name = RequireString(json, "counter");
            }
            else if (json["distinct"] != null)
            {
                condition.Kind = AchievementConditionKind.Distinct;
                condition.Name = RequireString(json, "distinct");
            }
            else
                throw new FormatException($"unknown condition {json.ToString(Newtonsoft.Json.Formatting.None)}");

            return condition;
        }

        private static AchievementCondition ParseGroup(AchievementConditionKind kind, JArray children)
        {
            var condition = new AchievementCondition { Kind = kind };
            foreach (var child in children)
                condition.Children.Add(ParseCondition(child as JObject));

            if (condition.Children.Count == 0)
                throw new FormatException($"{kind} has no conditions");
            return condition;
        }

        private static string RequireString(JObject json, string key)
        {
            var value = json.Value<string>(key);
            if (string.IsNullOrEmpty(value))
                throw new FormatException($"\"{key}\" is empty");
            return value;
        }

        private static void ParseNames(JToken token, AchievementCondition condition)
        {
            if (token is JArray names)
            {
                foreach (var name in names)
                    condition.Names.Add(name.Value<string>());
            }
            else
                condition.Names.Add(token.Value<string>());

            if (condition.Names.Count == 0 || condition.Names.Exists(string.IsNullOrEmpty))
                throw new FormatException($"{condition.Kind} needs names");
        }

        private static void ParseValue(JObject json, AchievementCondition condition)
        {
            var value = json["value"];
            if (value == null)
                throw new FormatException("\"value\" is missing");

            if (value.Type == JTokenType.Integer)
                condition.Value = value.Value<int>();
            else
                condition.ValueName = value.Value<string>();
        }

        private static int ParseTopicStatus(string status)
        {
            return status.ToLowerInvariant() switch
            {
                "free" => (int)SaveTopicStatus.Free,
                "running" => (int)SaveTopicStatus.Running,
                "success" => (int)SaveTopicStatus.Success,
                "failure" => (int)SaveTopicStatus.Failure,
                "obsolete" => (int)SaveTopicStatus.Obsolete,
                _ => throw new FormatException($"unknown log topic status {status}")
            };
        }

        private static AchievementCompareOp ParseOp(string op)
        {
            return op switch
            {
                "==" => AchievementCompareOp.Equal,
                "!=" => AchievementCompareOp.NotEqual,
                ">" => AchievementCompareOp.Greater,
                ">=" => AchievementCompareOp.GreaterOrEqual,
                "<" => AchievementCompareOp.Less,
                "<=" => AchievementCompareOp.LessOrEqual,
                _ => throw new FormatException($"unknown op {op}")
            };
        }

        private static void AssignKillCounters(AchievementCondition condition, string id, ref int killedCount)
        {
            if (condition.Kind == AchievementConditionKind.Killed)
                condition.KillCounterKey = $"kill:{id}:{killedCount++}";

            foreach (var child in condition.Children)
                AssignKillCounters(child, id, ref killedCount);
        }

        private static AchievementCondition FindProgressCondition(AchievementCondition condition)
        {
            if (condition.Kind is AchievementConditionKind.Counter or AchievementConditionKind.Distinct ||
                (condition.Kind is AchievementConditionKind.Killed or AchievementConditionKind.Item &&
                 condition.Count > 1))
                return condition;

            foreach (var child in condition.Children)
            {
                var progress = FindProgressCondition(child);
                if (progress != null)
                    return progress;
            }

            return null;
        }
    }
}
