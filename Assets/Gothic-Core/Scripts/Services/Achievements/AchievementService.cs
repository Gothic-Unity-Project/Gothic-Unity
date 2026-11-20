using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Gothic.Core.Const;
using Gothic.Core.Domain.Achievements;
using Gothic.Core.Domain.Npc;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Achievements;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Doc;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.World;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Achievements
{
    /// <summary>
    /// Local achievements (Docs/achievements.md). Facts arrive through GlobalEventDispatcher and check only the
    /// definitions indexed for them; pure variable conditions are re-checked on the in-game minute tick. Facts are
    /// ignored from LoadGameStart/LoadingSceneLoaded until OnWorldReady(): loading unlocks nothing by itself.
    ///
    /// Usage:
    /// * Increment(counterId) / AddDistinct(counterId, key) -> counters of Core and modules (AchievementCounterIds).
    /// * GetEntries() / GetCompletion() -> read model for the UI. AchievementUnlocked tells when it changed.
    /// </summary>
    public class AchievementService
    {
        private const string _buildKeyResourcePath = "Achievements/AchievementsSealKey";

        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly MarvinService _marvinService;

        private readonly AchievementDefinitionDomain _definitionDomain = new AchievementDefinitionDomain().Inject();
        private readonly AchievementProfileDomain _profileDomain = new AchievementProfileDomain().Inject();
        private readonly AchievementStateReaderDomain _reader = new AchievementStateReaderDomain().Inject();
        private readonly DamagePerceptionDomain _damagePerceptionDomain = new DamagePerceptionDomain().Inject();
        private readonly AchievementConditionDomain _conditionDomain = new();
        private readonly AchievementRunDomain _runDomain = new();
        private readonly AchievementSealDomain _sealDomain = new();
        private readonly AchievementReadModelDomain _readModelDomain = new();

        /// <summary>
        /// GameSettings.json "AchievementsEnabled". Off: nothing is recorded, no events.
        /// </summary>
        public bool IsEnabled { get; private set; }

        private byte[] _key;
        private string _profileKey;
        private AchievementProfileData _profile = new();
        private List<AchievementDefinition> _definitions = new();
        private AchievementTriggerIndex _index = new();
        private bool _isResolved;

        private AchievementRunState _run;
        private bool _isRunPending;
        private AchievementRunState _restoredRun;
        private bool _isRestoredSealed = true;
        private int _capturedSaveNo = -1;

        private bool _isWorldReady;
        private int _ignoredFacts;
        private readonly Dictionary<int, int> _variableSnapshot = new();


        public void Init()
        {
            IsEnabled = _configService.Root?.AchievementsEnabled ?? true;
            if (!IsEnabled)
            {
                Logger.Log("[Achievements] Off (GameSettings.json AchievementsEnabled).", LogCat.Achievements);
                return;
            }

            var folder = Path.Combine(Application.persistentDataPath, AchievementProfileDomain.FolderName);
            var deviceId = AchievementProfileDomain.ReadOrCreateDeviceId(folder);
            _key = AchievementSealDomain.CreateKey(ReadBuildKey(), deviceId);

            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(CloseGate);
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(OnWorldSceneLoaded);
            GlobalEventDispatcher.GameTimeMinuteChangeCallback.AddListener(OnGameMinute);
            GlobalEventDispatcher.LogTopicStatusChanged.AddListener(OnLogTopicStatusChanged);
            GlobalEventDispatcher.HeroTrueGuildChanged.AddListener(OnHeroTrueGuildChanged);
            GlobalEventDispatcher.DialogInfoTold.AddListener(OnDialogInfoTold);
            GlobalEventDispatcher.HeroItemsChanged.AddListener(OnHeroItemsChanged);
            GlobalEventDispatcher.NpcKilled.AddListener(OnNpcKilled);
            GlobalEventDispatcher.LockPickComboFinished.AddListener(OnLockOpened);
            GlobalEventDispatcher.DocShow.AddListener(OnDocShown);
            GlobalEventDispatcher.MarvinModeChanged.AddListener(OnMarvinModeChanged);
            GlobalEventDispatcher.HeroDied.AddListener(OnHeroDied);
        }

        /// <summary>
        /// Writes what's pending and removes the listeners (application quit).
        /// </summary>
        public void Teardown()
        {
            if (!IsEnabled)
                return;

            Flush();
            GlobalEventDispatcher.LoadGameStart.RemoveListener(OnLoadGameStart);
            GlobalEventDispatcher.LoadingSceneLoaded.RemoveListener(CloseGate);
            GlobalEventDispatcher.WorldSceneLoaded.RemoveListener(OnWorldSceneLoaded);
            GlobalEventDispatcher.GameTimeMinuteChangeCallback.RemoveListener(OnGameMinute);
            GlobalEventDispatcher.LogTopicStatusChanged.RemoveListener(OnLogTopicStatusChanged);
            GlobalEventDispatcher.HeroTrueGuildChanged.RemoveListener(OnHeroTrueGuildChanged);
            GlobalEventDispatcher.DialogInfoTold.RemoveListener(OnDialogInfoTold);
            GlobalEventDispatcher.HeroItemsChanged.RemoveListener(OnHeroItemsChanged);
            GlobalEventDispatcher.NpcKilled.RemoveListener(OnNpcKilled);
            GlobalEventDispatcher.LockPickComboFinished.RemoveListener(OnLockOpened);
            GlobalEventDispatcher.DocShow.RemoveListener(OnDocShown);
            GlobalEventDispatcher.MarvinModeChanged.RemoveListener(OnMarvinModeChanged);
            GlobalEventDispatcher.HeroDied.RemoveListener(OnHeroDied);
            IsEnabled = false;
        }

        /// <summary>
        /// Pending profile changes are written now (quit, pause/headset taken off).
        /// </summary>
        public void Flush()
        {
            if (IsEnabled)
                _profileDomain.Flush();
        }

        /// <summary>
        /// Editor and self-made builds use the committed dev key, CI builds the key from a GitHub secret.
        /// </summary>
        private byte[] ReadBuildKey()
        {
            var text = _resourceCacheService.TryGetTextAsset(_buildKeyResourcePath)?.text?.Trim();
            if (!string.IsNullOrEmpty(text))
                return Encoding.UTF8.GetBytes(text);

            Logger.LogWarning($"[Achievements] Build key Resources/{_buildKeyResourcePath} is missing. Seals use an " +
                              "empty key.", LogCat.Achievements);
            return Array.Empty<byte>();
        }

        private void OnLoadGameStart()
        {
            CloseGate();
            _isRunPending = true;
            _run = null;
        }

        private void CloseGate()
        {
            _isWorldReady = false;
        }

        /// <summary>
        /// The world exists, its scripts and save sections ran. Still behind the loading screen: the gate stays
        /// closed until OnWorldReady().
        /// </summary>
        private void OnWorldSceneLoaded()
        {
            EnsureLoaded();
            if (!_isRunPending)
                return;

            _isRunPending = false;
            var saveGameService = ReflexProjectInstaller.DIContainer.Resolve<SaveGameService>();
            _run = saveGameService.IsNewGame
                ? _runDomain.StartNewGame(_profile, _marvinService.IsMarvinMode)
                : _runDomain.StartLoadedGame(_profile, _restoredRun, _isRestoredSealed, _marvinService.IsMarvinMode);
            _restoredRun = null;
            _profileDomain.ScheduleWrite(_profile);
        }

        /// <summary>
        /// Called by WorldScene after the loading screen is hidden: facts count from now on. The variable values of
        /// this moment are the base line - only a change during play unlocks a variable condition.
        /// </summary>
        public void OnWorldReady()
        {
            if (!IsEnabled || _run == null)
                return;

            _variableSnapshot.Clear();
            foreach (var definition in _index.VariableDefinitions)
                SnapshotVariables(definition.Condition);

            if (_ignoredFacts > 0)
                Logger.Log($"[Achievements] {_ignoredFacts} fact(s) while loading were ignored.", LogCat.Achievements);
            _ignoredFacts = 0;
            _isWorldReady = true;
        }

        /// <summary>
        /// Profile and definitions of the running game (and mod). Script symbols are resolved once a VM exists.
        /// </summary>
        private void EnsureLoaded()
        {
            if (_configService.Gothic == null)
                return; // No game selected yet.

            var profileKey = $"{_contextGameVersionService.Version}{_configService.ModCacheSuffix}";
            if (profileKey != _profileKey)
            {
                _profileKey = profileKey;
                var folder = Path.Combine(Application.persistentDataPath, AchievementProfileDomain.FolderName);
                _profile = _profileDomain.Load(Path.Combine(folder, $"{profileKey}.json"), _key);
                _definitions = ReadDefinitions();
                _isResolved = false;
                _index = new AchievementTriggerIndex();
            }

            if (_isResolved || _gameStateService.GothicVm == null)
                return;

            _definitionDomain.Resolve(_definitions, _gameStateService.GothicVm);
            _index = AchievementDefinitionDomain.BuildIndex(_definitions);
            _isResolved = true;
            Logger.Log($"[Achievements] {_definitions.Count} definitions for profile {_profileKey}.",
                LogCat.Achievements);
        }

        private List<AchievementDefinition> ReadDefinitions()
        {
            var text = _definitionDomain.ReadDefinitionFile(_contextGameVersionService.Version);
            if (text == null)
                return new List<AchievementDefinition>();

            var errors = new List<string>();
            var definitions = AchievementDefinitionDomain.Parse(text, errors);
            foreach (var error in errors)
                Logger.LogWarning($"[Achievements] Definition skipped: {error}", LogCat.Achievements);
            return definitions;
        }

        private bool CanRecord()
        {
            if (_isWorldReady && _run != null)
                return true;

            _ignoredFacts++;
            return false;
        }

        private void OnLogTopicStatusChanged(string topic, SaveTopicStatus status)
        {
            Check(new AchievementFact(AchievementFactKind.LogTopic, stringKey: topic, value: (int)status));
        }

        private void OnHeroTrueGuildChanged(int guild)
        {
            Check(new AchievementFact(AchievementFactKind.TrueGuild, value: guild));
        }

        private void OnDialogInfoTold(int infoIndex)
        {
            Check(new AchievementFact(AchievementFactKind.InfoTold, infoIndex));
        }

        private void OnHeroItemsChanged(int itemIndex)
        {
            Check(new AchievementFact(AchievementFactKind.HeroItem, itemIndex));
        }

        private void OnNpcKilled(NpcContainer victim, NpcContainer killer)
        {
            if (victim?.Instance == null)
                return;

            var creditedKiller = _damagePerceptionDomain.GetCreditedAttacker(killer);
            var isByHero = creditedKiller != victim && creditedKiller?.Instance != null &&
                           creditedKiller.Instance.IsHero(_gameStateService.GothicVm);
            Check(new AchievementFact(AchievementFactKind.Kill, victim.Instance.Index, isByHero: isByHero));
        }

        /// <summary>
        /// LockPickComboFinished is any opened lock (also by key) - only a lock pick counts.
        /// </summary>
        private void OnLockOpened(VobContainer tool, VobContainer lockable, int hand)
        {
            if (tool?.Vob is IItem item && Constants.Items.IsLockPick(item.GetSymbolName()))
                Increment(AchievementCounterIds.LockpickSuccess);
        }

        private void OnDocShown(DocModel doc, GameObject target)
        {
            if (doc != null)
                AddDistinct(AchievementCounterIds.DocumentShown, doc.Identity);
        }

        private void OnMarvinModeChanged(bool isActive)
        {
            if (_run != null)
                _runDomain.ApplyMarvinMode(_run, isActive);
        }

        private void OnHeroDied(NpcContainer killer)
        {
            if (_run == null)
                return;

            _runDomain.MarkSpent(_profile, _run);
            _profileDomain.ScheduleWrite(_profile);
        }

        /// <summary>
        /// A counter of Core or a module rose (AchievementCounterIds, "vr.*", "{modId}.*").
        /// </summary>
        public void Increment(string counterId)
        {
            if (!IsEnabled || string.IsNullOrEmpty(counterId) || !CanRecord())
                return;

            _run.Increment(counterId);
            Check(new AchievementFact(AchievementFactKind.Counter, stringKey: counterId));
        }

        /// <summary>
        /// "N different X": key counts once per run (e.g. a document's identity).
        /// </summary>
        public void AddDistinct(string counterId, string key)
        {
            if (!IsEnabled || string.IsNullOrEmpty(counterId) || string.IsNullOrEmpty(key) || !CanRecord())
                return;

            if (_run.AddDistinct(counterId, key))
                Check(new AchievementFact(AchievementFactKind.Counter, stringKey: counterId));
        }

        private void Check(in AchievementFact fact)
        {
            if (!CanRecord())
                return;

            foreach (var definition in _index.Get(fact))
            {
                if (fact.Kind == AchievementFactKind.Kill)
                    _conditionDomain.CountKill(definition.Condition, fact, _run);

                if (!IsEarned(definition) && _conditionDomain.IsMet(definition.Condition, fact, _reader, _run))
                    Unlock(definition);
            }
        }

        private void OnGameMinute(DateTime time)
        {
            if (!_isWorldReady || _run == null)
                return;

            var fact = new AchievementFact(AchievementFactKind.VariableTick);
            foreach (var definition in _index.VariableDefinitions)
            {
                if (!IsEarned(definition) && HasVariableChanged(definition.Condition) &&
                    _conditionDomain.IsMet(definition.Condition, fact, _reader, _run))
                    Unlock(definition);
            }
        }

        private void SnapshotVariables(AchievementCondition condition)
        {
            if (condition.Kind == AchievementConditionKind.Variable)
                _variableSnapshot[condition.ResolvedIndex] = _reader.GetVariable(condition.ResolvedIndex);

            foreach (var child in condition.Children)
                SnapshotVariables(child);
        }

        private bool HasVariableChanged(AchievementCondition condition)
        {
            if (condition.Kind == AchievementConditionKind.Variable)
                return !_variableSnapshot.TryGetValue(condition.ResolvedIndex, out var value) ||
                       _reader.GetVariable(condition.ResolvedIndex) != value;

            foreach (var child in condition.Children)
            {
                if (HasVariableChanged(child))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Earned already in this tier or a better one, with an intact seal.
        /// </summary>
        private bool IsEarned(AchievementDefinition definition)
        {
            var unlock = _profile.FindUnlock(definition.Id);
            return unlock != null && unlock.IsSealed && unlock.Tier >= _run.Tier;
        }

        private void Unlock(AchievementDefinition definition)
        {
            var previous = _profile.FindUnlock(definition.Id);
            if (previous != null)
                _profile.Unlocks.Remove(previous);

            var tier = _run.Tier;
            _profile.Unlocks.Add(new AchievementUnlock
            {
                Id = definition.Id,
                Tier = tier,
                DateUtc = DateTime.UtcNow.ToString("O"),
                RunId = _run.RunId,
                SaveNo = _run.SaveNo,
                IsSealed = true
            });
            _profileDomain.ScheduleWrite(_profile);

            Logger.Log($"[Achievements] Unlocked {definition.Id} ({tier}).", LogCat.Achievements);
            GlobalEventDispatcher.AchievementUnlocked.Invoke(definition, tier);
        }

        /// <summary>
        /// Save section core.achievements: the sealed run state with the number this save gets. It only counts as the
        /// run's newest once OnSaveCommitted() confirms the save was written.
        /// </summary>
        public JToken CaptureRun()
        {
            _capturedSaveNo = -1;
            if (!IsEnabled || _run == null)
                return null;

            var currentSaveNo = _run.SaveNo;
            _capturedSaveNo = _runDomain.GetNextSaveNo(_profile, _run);
            _run.SaveNo = _capturedSaveNo;
            try
            {
                return _sealDomain.SealSection(_key, _run, GetSaveIdentity());
            }
            finally
            {
                _run.SaveNo = currentSaveNo;
            }
        }

        /// <summary>
        /// SaveGameService.SaveCurrentGame(): the save captured last was written successfully.
        /// </summary>
        public void OnSaveCommitted()
        {
            if (!IsEnabled || _run == null || _capturedSaveNo < 0)
                return;

            _runDomain.CommitSave(_profile, _run, _capturedSaveNo);
            _capturedSaveNo = -1;
            _profileDomain.ScheduleWrite(_profile);
        }

        public void ResetRun()
        {
            _restoredRun = null;
            _isRestoredSealed = true;
            _capturedSaveNo = -1;
        }

        /// <summary>
        /// A loaded save's section. A broken seal plays as Beliar (OnWorldSceneLoaded decides the run).
        /// </summary>
        public void RestoreRun(JToken data)
        {
            if (!IsEnabled || data == null || data.Type == JTokenType.Null)
                return;

            _isRestoredSealed = _sealDomain.TryOpenSection(_key, data, GetSaveIdentity(), out _restoredRun);
            if (!_isRestoredSealed)
                Logger.LogWarning("[Achievements] The save's achievement data has a broken seal (edited, copied or " +
                                  "from another device/build). This game plays as Beliar.", LogCat.Achievements);
        }

        /// <summary>
        /// Gothic's SAVEINFO of the save being written or loaded (date + play time): binds a section to its save.
        /// </summary>
        private static string GetSaveIdentity()
        {
            var metadata = ReflexProjectInstaller.DIContainer.Resolve<SaveGameService>().Save?.Metadata;
            if (metadata == null)
                return string.Empty;

            return $"{metadata.SaveDate.Trim()}|{(int)metadata.PlayTime.TotalSeconds}";
        }

        /// <summary>
        /// Every achievement of the current game with texts in the current language and its state. Built on each call
        /// (UI opens), not cached.
        /// </summary>
        public List<AchievementEntry> GetEntries()
        {
            if (IsEnabled)
                EnsureLoaded();

            return _readModelDomain.CreateEntries(_definitions, _profile, _run, _isWorldReady ? _reader : null,
                _gameStateService.Language);
        }

        /// <summary>
        /// Sealed unlocks (unsealed ones don't count) out of all definitions.
        /// </summary>
        public void GetCompletion(out int unlocked, out int total)
        {
            if (IsEnabled)
                EnsureLoaded();

            _readModelDomain.GetCompletion(_definitions, _profile, out unlocked, out total);
        }

        /// <summary>
        /// The tier of the running game (Innos/Adanos/Beliar), null outside a game.
        /// </summary>
        public AchievementTier? CurrentTier => _run?.Tier;
    }
}
