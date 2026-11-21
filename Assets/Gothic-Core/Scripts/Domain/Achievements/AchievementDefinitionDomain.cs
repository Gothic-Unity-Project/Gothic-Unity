using System.Collections.Generic;
using System.IO;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Achievements;
using Gothic.Core.Services.Config;
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
    /// own &lt;modFolder&gt;/Achievements.json or system/&lt;ini&gt;.achievements.json (parsed by
    /// AchievementDefinitionParser). Resolves their script symbols (a missing one makes the definition unavailable,
    /// one warning) and builds the trigger index.
    /// </summary>
    public class AchievementDefinitionDomain
    {
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
            var definitions = AchievementDefinitionParser.Parse(text, errors);
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
                var fileName = version == GameVersion.Gothic1
                    ? Constants.Achievements.G1FileName
                    : Constants.Achievements.G2FileName;
                var path = Path.Combine(Application.streamingAssetsPath, Constants.Achievements.FolderName, fileName);
                var text = ReadStreamingAsset(path);
                if (text == null)
                    Logger.LogWarning($"[Achievements] {path} is missing or can't be read: no achievements.",
                        LogCat.Achievements);
                return text;
            }

            var modPath = _configService.EffectiveModPath;
            var modFile = string.IsNullOrEmpty(modPath)
                ? null
                : PathExtension.FindIgnoringCase(modPath, Constants.Achievements.ModFileName);
            if (modFile != null)
                return File.ReadAllText(modFile);

            var iniPath = _configService.GothicMod?.IniFilePath;
            var iniFile = string.IsNullOrEmpty(iniPath)
                ? null
                : PathExtension.FindIgnoringCase(Path.GetDirectoryName(iniPath),
                    Path.GetFileNameWithoutExtension(iniPath) + Constants.Achievements.ModIniFileSuffix);
            if (iniFile != null)
                return File.ReadAllText(iniFile);

            Logger.Log($"[Achievements] The mod has no own list ({Constants.Achievements.ModFileName} or " +
                       $"<ini>{Constants.Achievements.ModIniFileSuffix}): no achievements.", LogCat.Achievements);
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
