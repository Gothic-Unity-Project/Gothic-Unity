using System;
using System.Collections.Generic;
using System.IO;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Achievements;
using Gothic.Core.Services.Config;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Networking;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// Loads the definitions: StreamingAssets/Achievements/g1.json|g2.json, or with a mod (base lists off) the mod's
    /// own &lt;modFolder&gt;/Achievements.json or system/&lt;ini&gt;.achievements.json. Resolves their script symbols
    /// (a missing one makes the definition unavailable, one warning) and builds the trigger index.
    /// </summary>
    public class AchievementDefinitionDomain
    {
        private const string _streamingAssetsFolder = "Achievements";
        private const string _modFileName = "Achievements.json";
        private const string _modIniFileSuffix = ".achievements.json";

        [Inject] private readonly ConfigService _configService;

        /// <summary>
        /// The definitions of the running game or mod (Load()), empty if it has none.
        /// </summary>
        public List<AchievementDefinition> Definitions { get; private set; } = new();

        /// <summary>
        /// Which facts can complete which definition. Empty until EnsureResolved() found a VM.
        /// </summary>
        public AchievementTriggerIndex Index { get; private set; } = new();

        private bool _isResolved;


        /// <summary>
        /// Reads the definitions of the running game or mod anew. Their symbols are resolved by EnsureResolved().
        /// Throws on IO errors.
        /// </summary>
        public void Load(GameVersion version)
        {
            Definitions = ReadDefinitions(version);
            Index = new AchievementTriggerIndex();
            _isResolved = false;
        }

        /// <summary>
        /// Resolves the script symbols and builds the index once a VM exists. True if it happened now.
        /// </summary>
        public bool EnsureResolved(DaedalusVm vm)
        {
            if (_isResolved || vm == null)
                return false;

            Resolve(Definitions, vm);
            Index = BuildIndex(Definitions);
            _isResolved = true;
            return true;
        }

        /// <summary>
        /// One that doesn't parse is skipped with a warning.
        /// </summary>
        private List<AchievementDefinition> ReadDefinitions(GameVersion version)
        {
            var text = ReadDefinitionFile(version);
            if (text == null)
                return new List<AchievementDefinition>();

            var errors = new List<string>();
            var definitions = Parse(text, errors);
            foreach (var error in errors)
                Logger.LogWarning($"[Achievements] Definition skipped: {error}", LogCat.Achievements);
            return definitions;
        }

        /// <summary>
        /// The file's text, or null if there is none (a mod without its own list: logged, a missing vanilla list is a
        /// warning). Throws on IO errors.
        /// </summary>
        private string ReadDefinitionFile(GameVersion version)
        {
            if (!_configService.IsModActive)
            {
                var fileName = version == GameVersion.Gothic1 ? "g1.json" : "g2.json";
                var path = Path.Combine(Application.streamingAssetsPath, _streamingAssetsFolder, fileName);
                var text = ReadStreamingAsset(path);
                if (text == null)
                    Logger.LogWarning($"[Achievements] {path} is missing or can't be read: no achievements.",
                        LogCat.Achievements);
                return text;
            }

            var modPath = _configService.EffectiveModPath;
            var modFile = string.IsNullOrEmpty(modPath) ? null : PathExtension.FindIgnoringCase(modPath, _modFileName);
            if (modFile != null)
                return File.ReadAllText(modFile);

            var iniPath = _configService.GothicMod?.IniFilePath;
            var iniFile = string.IsNullOrEmpty(iniPath)
                ? null
                : PathExtension.FindIgnoringCase(Path.GetDirectoryName(iniPath),
                    Path.GetFileNameWithoutExtension(iniPath) + _modIniFileSuffix);
            if (iniFile != null)
                return File.ReadAllText(iniFile);

            Logger.Log($"[Achievements] The mod has no own list ({_modFileName} or <ini>{_modIniFileSuffix}): no " +
                       "achievements.", LogCat.Achievements);
            return null;
        }

        /// <summary>
        /// Android keeps StreamingAssets inside the APK - only UnityWebRequest reads them there.
        /// </summary>
        private static string ReadStreamingAsset(string path)
        {
            if (Application.platform != RuntimePlatform.Android)
                return File.Exists(path) ? File.ReadAllText(path) : null;

            using var request = UnityWebRequest.Get(path);
            request.SendWebRequest();
            while (!request.isDone)
            { }

            return request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null;
        }

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

        /// <summary>
        /// Resolves the script symbols of every definition. A missing symbol makes it unavailable (one warning each).
        /// </summary>
        public void Resolve(List<AchievementDefinition> definitions, DaedalusVm vm)
        {
            foreach (var definition in definitions)
            {
                if (TryResolve(definition.Condition, vm, out var missing))
                    continue;

                definition.IsUnavailable = true;
                Logger.LogWarning($"[Achievements] {definition.Id} is unavailable: the scripts have no {missing}.",
                    LogCat.Achievements);
            }
        }

        private static bool TryResolve(AchievementCondition condition, DaedalusVm vm, out string missing)
        {
            missing = null;
            switch (condition.Kind)
            {
                case AchievementConditionKind.All:
                case AchievementConditionKind.Any:
                    foreach (var child in condition.Children)
                    {
                        if (!TryResolve(child, vm, out missing))
                            return false;
                    }
                    return true;
                case AchievementConditionKind.LogTopic:
                    var topic = FindSymbol(vm, condition.Name, DaedalusDataType.String, true);
                    condition.ResolvedText = topic?.GetString(0);
                    missing = topic == null ? $"string constant {condition.Name}" : null;
                    return topic != null;
                case AchievementConditionKind.Variable:
                    var variable = FindSymbol(vm, condition.Name, DaedalusDataType.Int, false);
                    condition.ResolvedIndex = variable?.Index ?? -1;
                    missing = variable == null ? $"int variable {condition.Name}" : null;
                    return variable != null && TryResolveValue(condition, vm, out missing);
                case AchievementConditionKind.TrueGuild:
                    condition.ResolvedIndices.Clear();
                    foreach (var name in condition.Names)
                    {
                        if (!TryResolveConstant(vm, name, out var guild))
                        {
                            missing = $"int constant {name}";
                            return false;
                        }
                        condition.ResolvedIndices.Add(guild);
                    }
                    return true;
                case AchievementConditionKind.InfoTold:
                case AchievementConditionKind.Item:
                    return TryResolveIndex(vm, condition.Name, out condition.ResolvedIndex, out missing);
                case AchievementConditionKind.Killed:
                    condition.ResolvedIndices.Clear();
                    foreach (var name in condition.Names)
                    {
                        if (!TryResolveIndex(vm, name, out var index, out missing))
                            return false;
                        condition.ResolvedIndices.Add(index);
                    }
                    return true;
                case AchievementConditionKind.NpcAivar:
                    if (!TryResolveIndex(vm, condition.Name, out condition.ResolvedIndex, out missing))
                        return false;
                    if (!int.TryParse(condition.AivarName, out condition.ResolvedAivarIndex) &&
                        !TryResolveConstant(vm, condition.AivarName, out condition.ResolvedAivarIndex))
                    {
                        missing = $"int constant {condition.AivarName}";
                        return false;
                    }
                    return TryResolveValue(condition, vm, out missing);
                default:
                    return true;
            }
        }

        private static bool TryResolveValue(AchievementCondition condition, DaedalusVm vm, out string missing)
        {
            missing = null;
            if (condition.ValueName == null)
                return true;

            if (TryResolveConstant(vm, condition.ValueName, out condition.Value))
                return true;

            missing = $"int constant {condition.ValueName}";
            return false;
        }

        private static bool TryResolveIndex(DaedalusVm vm, string name, out int index, out string missing)
        {
            var symbol = vm.GetSymbolByName(name);
            index = symbol?.Index ?? -1;
            missing = symbol == null ? $"symbol {name}" : null;
            return symbol != null;
        }

        private static bool TryResolveConstant(DaedalusVm vm, string name, out int value)
        {
            var symbol = FindSymbol(vm, name, DaedalusDataType.Int, true);
            value = symbol?.GetInt(0) ?? 0;
            return symbol != null;
        }

        private static DaedalusSymbol FindSymbol(DaedalusVm vm, string name, DaedalusDataType type, bool isConst)
        {
            var symbol = vm.GetSymbolByName(name);
            return symbol != null && symbol.Type == type && symbol.IsConst == isConst && !symbol.IsMember
                ? symbol
                : null;
        }

        /// <summary>
        /// Which facts can complete which definition. Unavailable definitions aren't indexed.
        /// </summary>
        public static AchievementTriggerIndex BuildIndex(List<AchievementDefinition> definitions)
        {
            var index = new AchievementTriggerIndex();
            foreach (var definition in definitions)
            {
                if (definition.IsUnavailable)
                    continue;

                if (definition.Condition.IsPureVariable)
                    index.VariableDefinitions.Add(definition);
                else
                    AddTriggers(index, definition.Condition, definition);
            }

            return index;
        }

        private static void AddTriggers(AchievementTriggerIndex index, AchievementCondition condition,
            AchievementDefinition definition)
        {
            switch (condition.Kind)
            {
                case AchievementConditionKind.All:
                case AchievementConditionKind.Any:
                    foreach (var child in condition.Children)
                        AddTriggers(index, child, definition);
                    break;
                case AchievementConditionKind.LogTopic:
                    index.Add(AchievementFactKind.LogTopic, condition.ResolvedText ?? condition.Name, definition);
                    break;
                case AchievementConditionKind.TrueGuild:
                    index.Add(AchievementFactKind.TrueGuild, definition);
                    break;
                case AchievementConditionKind.InfoTold:
                    index.Add(AchievementFactKind.InfoTold, condition.ResolvedIndex, definition);
                    break;
                case AchievementConditionKind.Item:
                    index.Add(AchievementFactKind.HeroItem, condition.ResolvedIndex, definition);
                    break;
                case AchievementConditionKind.Killed:
                    foreach (var victim in condition.ResolvedIndices)
                        index.Add(AchievementFactKind.Kill, victim, definition);
                    break;
                case AchievementConditionKind.Counter:
                case AchievementConditionKind.Distinct:
                    index.Add(AchievementFactKind.Counter, condition.Name, definition);
                    break;
            }
        }
    }
}
