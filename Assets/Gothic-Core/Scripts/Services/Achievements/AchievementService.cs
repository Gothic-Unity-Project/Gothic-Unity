using System;
using System.Collections.Generic;
using Gothic.Core.Domain.Achievements;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Achievements;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Achievements
{
    /// <summary>
    /// Local achievements (Docs/achievements.md). Facts arrive through GlobalEventDispatcher (AchievementFactDomain)
    /// and check only the definitions indexed for them. Pure variable conditions are re-checked a frame after the
    /// script moments that change story variables (dialog info, death, item, mob, trigger, chapter), and by
    /// RecheckNow() (help panel, achievements page, save). Facts are ignored from LoadGameStart/LoadingSceneLoaded
    /// until OnWorldReady(): loading unlocks nothing by itself. The run and its save bookkeeping live in
    /// AchievementRunDomain.
    ///
    /// Usage:
    /// * Increment(counterId) / AddDistinct(counterId, key) -> counters of Core and modules (AchievementCounterIds).
    /// * GetEntries() / GetCompletion() -> read model for the UI. AchievementUnlocked tells when it changed.
    /// </summary>
    public class AchievementService
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly MarvinService _marvinService;

        private readonly AchievementDefinitionDomain _definitionDomain = new AchievementDefinitionDomain().Inject();
        private readonly AchievementProfileDomain _profileDomain = new AchievementProfileDomain().Inject();
        private readonly AchievementStateReaderDomain _reader = new AchievementStateReaderDomain().Inject();
        private readonly AchievementFactDomain _factDomain = new AchievementFactDomain().Inject();
        private readonly AchievementConditionDomain _conditionDomain = new();
        private readonly AchievementRunDomain _runDomain = new();
        private readonly AchievementReadModelDomain _readModelDomain = new();
        private readonly AchievementVariableWatchDomain _variableWatch = new AchievementVariableWatchDomain().Inject();

        /// <summary>
        /// GameSettings.json "AchievementsEnabled". Off: nothing is recorded, no events.
        /// </summary>
        public bool IsEnabled { get; private set; }

        /// <summary>
        /// The tier of the running game (Adanos/Beliar), null outside a game.
        /// </summary>
        public AchievementTier? CurrentTier => Run?.Tier;

        private AchievementRunState Run => _runDomain.Run;
        private AchievementTriggerIndex Index => _definitionDomain.Index;

        private string _profileKey;
        private AchievementProfileData _profile = new();

        private bool _isWorldReady;
        private int _ignoredFacts;
        private bool _isVariableSnapshotPending = true;
        private bool _isCheckingVariables;
        private readonly List<AchievementDefinition> _metVariables = new();


        public void Init()
        {
            IsEnabled = _configService.Root?.AchievementsEnabled ?? true;
            if (!IsEnabled)
            {
                Logger.Log("[Achievements] Off (GameSettings.json AchievementsEnabled).", LogCat.Achievements);
                return;
            }

            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(CloseGate);
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(OnWorldSceneLoaded);
            GlobalEventDispatcher.MarvinModeChanged.AddListener(OnMarvinModeChanged);
            _factDomain.AddListeners(Check, Increment, AddDistinct, MarkVariablesDirty);
        }

        /// <summary>
        /// Writes what's pending and removes the listeners (application quit).
        /// </summary>
        public void Teardown()
        {
            if (!IsEnabled)
                return;

            Flush();
            RemoveListeners();
            IsEnabled = false;
        }

        private void RemoveListeners()
        {
            GlobalEventDispatcher.LoadGameStart.RemoveListener(OnLoadGameStart);
            GlobalEventDispatcher.LoadingSceneLoaded.RemoveListener(CloseGate);
            GlobalEventDispatcher.WorldSceneLoaded.RemoveListener(OnWorldSceneLoaded);
            GlobalEventDispatcher.MarvinModeChanged.RemoveListener(OnMarvinModeChanged);
            _factDomain.RemoveListeners();
        }

        /// <summary>
        /// An optional feature must not break game start or world load: a file error turns achievements off for this
        /// session.
        /// </summary>
        private void Disable(string what, Exception e)
        {
            Logger.LogError($"[Achievements] Off for this session: {what} failed ({e.Message}).", LogCat.Achievements);
            RemoveListeners();
            IsEnabled = false;
            _runDomain.EndRun();
            _isWorldReady = false;
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
        /// New game or save load (a fresh VM): the variable snapshot is taken anew at OnWorldReady(). A level change
        /// has no LoadGameStart and keeps the snapshot from before it.
        /// </summary>
        private void OnLoadGameStart()
        {
            CloseGate();
            _runDomain.BeginLoad();
            _isVariableSnapshotPending = true;
        }

        private void CloseGate()
        {
            _isWorldReady = false;
            _variableWatch.ClearDirty();
        }

        /// <summary>
        /// The world exists, its scripts and save sections ran. Still behind the loading screen: the gate stays
        /// closed until OnWorldReady().
        /// </summary>
        private void OnWorldSceneLoaded()
        {
            EnsureLoaded();
            if (IsEnabled)
                _runDomain.StartPendingRun(_marvinService.IsMarvinMode);
        }

        /// <summary>
        /// Called by WorldScene after the loading screen is hidden: facts count from now on.
        /// New game or loaded save: the variable values of this moment are the base line - only a change during play
        /// unlocks a variable condition (no retroactive unlocks). Level change: the snapshot from before it stays and
        /// one check runs now, so values the new world's STARTUP_/INIT_ scripts set count (G2: Kapitel 6 at
        /// Irdorath, ENTERED_ADDONWORLD in Jharkendar).
        /// </summary>
        public void OnWorldReady()
        {
            if (!IsEnabled || Run == null)
                return;

            var isLevelChange = !_isVariableSnapshotPending;
            if (_isVariableSnapshotPending)
            {
                _variableWatch.TakeSnapshot(Index.VariableDefinitions, _reader);
                _isVariableSnapshotPending = false;
            }

            if (_ignoredFacts > 0)
                Logger.Log($"[Achievements] {_ignoredFacts} fact(s) while loading were ignored.", LogCat.Achievements);
            _ignoredFacts = 0;
            _isWorldReady = true;

            if (isLevelChange)
                CheckVariables();
        }

        /// <summary>
        /// Profile and definitions of the running game (and mod). Script symbols are resolved once a VM exists.
        /// </summary>
        private void EnsureLoaded()
        {
            if (_configService.Gothic == null)
                return; // No game selected yet.

            try
            {
                LoadProfileAndDefinitions();
            }
            catch (Exception e)
            {
                Disable("loading the profile or the definitions", e);
            }
        }

        private void LoadProfileAndDefinitions()
        {
            var profileKey = $"{_contextGameVersionService.Version}{_configService.ModCacheSuffix}";
            if (profileKey != _profileKey)
            {
                _profileKey = profileKey;
                _profile = _profileDomain.Load(AchievementProfileDomain.GetProfilePath(profileKey));
                _definitionDomain.Load(_contextGameVersionService.Version);
            }

            if (_definitionDomain.EnsureResolved(_gameStateService.GothicVm))
                Logger.Log($"[Achievements] {_definitionDomain.Definitions.Count} definitions for profile " +
                           $"{_profileKey}.", LogCat.Achievements);
        }

        private bool CanRecord()
        {
            if (_isWorldReady && Run != null)
                return true;

            _ignoredFacts++;
            return false;
        }

        private void OnMarvinModeChanged(bool isActive)
        {
            if (Run != null)
                _runDomain.ApplyMarvinMode(Run, isActive);
        }

        /// <summary>
        /// A counter of Core or a module rose (AchievementCounterIds, "vr.*", "{modId}.*").
        /// </summary>
        public void Increment(string counterId)
        {
            if (!IsEnabled || string.IsNullOrEmpty(counterId) || !CanRecord())
                return;

            Run.Increment(counterId);
            Check(new AchievementFact(AchievementFactKind.Counter, stringKey: counterId));
        }

        /// <summary>
        /// "N different X": key counts once per run (e.g. a document's identity).
        /// </summary>
        public void AddDistinct(string counterId, string key)
        {
            if (!IsEnabled || string.IsNullOrEmpty(counterId) || string.IsNullOrEmpty(key) || !CanRecord())
                return;

            if (Run.AddDistinct(counterId, key))
                Check(new AchievementFact(AchievementFactKind.Counter, stringKey: counterId));
        }

        private void Check(AchievementFact fact)
        {
            if (!CanRecord())
                return;

            foreach (var definition in Index.Get(fact))
            {
                if (fact.Kind == AchievementFactKind.Kill)
                    _conditionDomain.CountKill(definition.Condition, fact, Run);

                if (!_profile.IsEarned(definition.Id, Run.Tier) &&
                    _conditionDomain.IsMet(definition.Condition, fact, _reader, Run))
                    Unlock(definition);
            }
        }

        /// <summary>
        /// A script moment that may have changed story variables. Several in one frame (a dialog and the item it
        /// gives) end in one check a frame later, when the calling script has finished too. Nothing while loading.
        /// </summary>
        private void MarkVariablesDirty()
        {
            if (_isWorldReady && Run != null && Index.VariableDefinitions.Count > 0)
                _variableWatch.ScheduleCheck(CheckVariables);
        }

        /// <summary>
        /// Fallback moments: the help panel or achievements page opens (and right before saving) - a story variable
        /// changed by a script we don't hook counts here at the latest.
        /// </summary>
        public void RecheckNow()
        {
            if (IsEnabled)
                CheckVariables();
        }

        private void CheckVariables()
        {
            // An unlock's listener (toast, page) may call RecheckNow() inside the loop below.
            if (_isCheckingVariables)
                return;

            if (!_isWorldReady || Run == null)
            {
                _variableWatch.ClearDirty();
                return;
            }

            _isCheckingVariables = true;
            try
            {
                _metVariables.Clear();
                _variableWatch.Check(Index.VariableDefinitions, _reader, Run, _profile, _metVariables);
                foreach (var definition in _metVariables)
                    Unlock(definition);
            }
            finally
            {
                _isCheckingVariables = false;
            }
        }

        private void Unlock(AchievementDefinition definition)
        {
            var tier = Run.Tier;
            _profile.SetUnlock(definition.Id, tier);
            _profileDomain.ScheduleWrite(_profile);

            Logger.Log($"[Achievements] Unlocked {definition.Id} ({tier}).", LogCat.Achievements);
            GlobalEventDispatcher.AchievementUnlocked.Invoke(definition, tier);
        }

        /// <summary>
        /// Save section core.achievements: the run's tier and counters.
        /// </summary>
        public JToken CaptureRun()
        {
            if (!IsEnabled)
                return null;

            // Fallback moment: what the scripts set until now is in the profile before the save is written.
            RecheckNow();
            return _runDomain.CaptureRun();
        }

        /// <summary>
        /// A new game starts or a save loads (RestoreRun() follows if it has achievement data).
        /// </summary>
        public void ResetRun(bool isNewGame)
        {
            _runDomain.ResetRun(isNewGame);
        }

        /// <summary>
        /// A loaded save's section (OnWorldSceneLoaded decides the run). Unreadable: plays like an older save.
        /// </summary>
        public void RestoreRun(JToken data)
        {
            if (!IsEnabled || data == null || data.Type == JTokenType.Null)
                return;

            if (!_runDomain.RestoreRun(data))
                Logger.LogWarning("[Achievements] The save's achievement data can't be read. This game plays as " +
                                  "Adanos with new counters.", LogCat.Achievements);
        }

        /// <summary>
        /// Every achievement of the current game with texts in the current language and its state. Built on each call
        /// (UI opens), not cached.
        /// </summary>
        public List<AchievementEntry> GetEntries()
        {
            if (IsEnabled)
                EnsureLoaded();

            return _readModelDomain.CreateEntries(_definitionDomain.Definitions, _profile, Run,
                _isWorldReady ? _reader : null, _gameStateService.Language);
        }

        /// <summary>
        /// Unlocks out of all definitions.
        /// </summary>
        public void GetCompletion(out int unlocked, out int total)
        {
            if (IsEnabled)
                EnsureLoaded();

            _readModelDomain.GetCompletion(_definitionDomain.Definitions, _profile, out unlocked, out total);
        }
    }
}
