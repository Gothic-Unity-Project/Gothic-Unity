using System;
using Gothic.Core.Models.Achievements;
using Newtonsoft.Json.Linq;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// The tier rules of a run (Docs/achievements.md, "Tiers"): a new game is Innos, going back to an older save or
    /// loading after the hero died is Adanos, Marvin mode is Beliar. The profile decides when save and profile
    /// disagree. The Marvin state is passed in (EditMode-testable).
    ///
    /// It also keeps the running game's run and its save bookkeeping: what the save section restored, whether a new
    /// game starts, and the save number of the last capture until the save is committed.
    /// </summary>
    public class AchievementRunDomain
    {
        private readonly AchievementSealDomain _sealDomain = new();

        private bool _isRunPending;
        private bool _isNewGame = true;
        private AchievementRunState _restoredRun;
        private bool _isRestoredSealed = true;
        private int _capturedSaveNo = -1;

        /// <summary>
        /// The running game's run. Null outside a game and while a new game or save loads (until StartPendingRun()).
        /// </summary>
        public AchievementRunState Run { get; private set; }


        /// <summary>
        /// LoadGameStart: a new game or a save loads, its run is decided once its first world is loaded.
        /// </summary>
        public void BeginLoad()
        {
            _isRunPending = true;
            Run = null;
        }

        /// <summary>
        /// No run any more (achievements turned off).
        /// </summary>
        public void EndRun()
        {
            _isRunPending = false;
            Run = null;
        }

        /// <summary>
        /// Save section Reset(): a new game starts or a save loads (RestoreRun() follows if it has the section).
        /// </summary>
        public void ResetRun(bool isNewGame)
        {
            _isNewGame = isNewGame;
            _restoredRun = null;
            _isRestoredSealed = true;
            _capturedSaveNo = -1;
        }

        /// <summary>
        /// A loaded save's section. False for a broken seal: the game plays as Beliar.
        /// </summary>
        public bool RestoreRun(byte[] key, JToken data, string saveIdentity)
        {
            _isRestoredSealed = _sealDomain.TryOpenSection(key, data, saveIdentity, out _restoredRun);
            return _isRestoredSealed;
        }

        /// <summary>
        /// The first world after BeginLoad() exists: the run of the new game, the restored section, or none (older
        /// save). False if no run was pending (level change).
        /// </summary>
        public bool StartPendingRun(AchievementProfileData profile, bool isMarvinMode)
        {
            if (!_isRunPending)
                return false;

            _isRunPending = false;
            Run = _isNewGame
                ? StartNewGame(profile, isMarvinMode)
                : StartLoadedGame(profile, _restoredRun, _isRestoredSealed, isMarvinMode);
            _restoredRun = null;
            return true;
        }

        /// <summary>
        /// The sealed run with the number this save gets. It only counts as the run's newest once
        /// CommitCapturedSave() confirms the save was written. Null without a run.
        /// </summary>
        public JToken CaptureRun(AchievementProfileData profile, byte[] key, string saveIdentity)
        {
            _capturedSaveNo = -1;
            if (Run == null)
                return null;

            var currentSaveNo = Run.SaveNo;
            _capturedSaveNo = GetNextSaveNo(profile, Run);
            Run.SaveNo = _capturedSaveNo;
            try
            {
                return _sealDomain.SealSection(key, Run, saveIdentity);
            }
            finally
            {
                Run.SaveNo = currentSaveNo;
            }
        }

        /// <summary>
        /// The save captured last was written. False if there was none (nothing changed in the profile).
        /// </summary>
        public bool CommitCapturedSave(AchievementProfileData profile)
        {
            if (Run == null || _capturedSaveNo < 0)
                return false;

            CommitSave(profile, Run, _capturedSaveNo);
            _capturedSaveNo = -1;
            return true;
        }

        /// <summary>
        /// The only place a run id is minted. The run is known to the profile from now on.
        /// </summary>
        public AchievementRunState StartNewGame(AchievementProfileData profile, bool isMarvinMode)
        {
            var run = CreateRun(profile, AchievementTier.Innos);
            ApplyMarvinMode(run, isMarvinMode);
            return run;
        }

        /// <summary>
        /// A loaded save. restored = its sealed section, null if it has none (older save) or isSealed is false.
        /// </summary>
        public AchievementRunState StartLoadedGame(AchievementProfileData profile, AchievementRunState restored,
            bool isSealed, bool isMarvinMode)
        {
            AchievementRunState run;
            if (!isSealed)
                run = CreateRun(profile, AchievementTier.Beliar);
            else if (restored == null)
                run = CreateRun(profile, AchievementTier.Adanos);
            else
            {
                run = restored;
                if (!IsNewestUnspentSave(profile, run))
                    run.LowerTierTo(AchievementTier.Adanos);
            }

            ApplyMarvinMode(run, isMarvinMode);
            return run;
        }

        /// <summary>
        /// Innos only continues from the run's newest save, and not after the hero died since it. An unknown run or a
        /// save number above the profile's newest (another device, an edited profile) counts as going back.
        /// </summary>
        public bool IsNewestUnspentSave(AchievementProfileData profile, AchievementRunState run)
        {
            return run.RunId != null && profile.Runs.TryGetValue(run.RunId, out var record) &&
                   run.SaveNo == record.NewestSaveNo && !record.IsSpent;
        }

        /// <summary>
        /// Turning Marvin mode on lowers the run to Beliar for good (also if turned off again).
        /// </summary>
        public void ApplyMarvinMode(AchievementRunState run, bool isMarvinMode)
        {
            if (isMarvinMode)
                run.LowerTierTo(AchievementTier.Beliar);
        }

        /// <summary>
        /// The number the next save gets: above every save of this run, also those written after going back.
        /// </summary>
        public int GetNextSaveNo(AchievementProfileData profile, AchievementRunState run)
        {
            var newest = profile.Runs.TryGetValue(run.RunId, out var record) ? record.NewestSaveNo : 0;
            return Math.Max(newest, run.SaveNo) + 1;
        }

        /// <summary>
        /// The save was written successfully: it's the run's newest now.
        /// </summary>
        public void CommitSave(AchievementProfileData profile, AchievementRunState run, int saveNo)
        {
            run.SaveNo = saveNo;
            var record = GetOrAddRecord(profile, run.RunId);
            record.NewestSaveNo = Math.Max(record.NewestSaveNo, saveNo);
            record.IsSpent = false;
        }

        /// <summary>
        /// The hero died: loading the newest save of this run afterwards is going back.
        /// </summary>
        public void MarkSpent(AchievementProfileData profile, AchievementRunState run)
        {
            GetOrAddRecord(profile, run.RunId).IsSpent = true;
        }

        private static AchievementRunState CreateRun(AchievementProfileData profile, AchievementTier tier)
        {
            var run = new AchievementRunState { RunId = Guid.NewGuid().ToString("N"), SaveNo = 0, Tier = tier };
            GetOrAddRecord(profile, run.RunId);
            return run;
        }

        private static AchievementRunRecord GetOrAddRecord(AchievementProfileData profile, string runId)
        {
            if (!profile.Runs.TryGetValue(runId, out var record))
            {
                record = new AchievementRunRecord();
                profile.Runs[runId] = record;
            }

            return record;
        }
    }
}
