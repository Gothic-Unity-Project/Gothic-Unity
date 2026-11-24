using System;
using System.Collections.Generic;
using Gothic.Core.Models.Achievements;
using Newtonsoft.Json.Linq;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// The tier rules of a run (Docs/achievements.md, "Tiers"): a new game is Adanos, Marvin mode lowers it to Beliar
    /// for good. A save remembers its run's tier and counters. The Marvin state is passed in (EditMode-testable).
    ///
    /// It also keeps the running game's run between a load's start and its first world: whether a new game starts
    /// and what the save section restored.
    /// </summary>
    public class AchievementRunDomain
    {
        private bool _isRunPending;
        private bool _isNewGame = true;
        private AchievementRunState _restoredRun;

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
        }

        /// <summary>
        /// A loaded save's section. False if it can't be read: the game plays like an older save without it.
        /// </summary>
        public bool RestoreRun(JToken data)
        {
            try
            {
                _restoredRun = data.ToObject<AchievementRunState>();
            }
            catch (Exception)
            {
                _restoredRun = null;
                return false;
            }

            if (_restoredRun == null)
                return false;

            _restoredRun.Counters ??= new Dictionary<string, int>();
            _restoredRun.Distinct ??= new Dictionary<string, HashSet<string>>();
            return true;
        }

        /// <summary>
        /// The first world after BeginLoad() exists: the run of the new game, the restored section, or a new one
        /// (older save). False if no run was pending (level change).
        /// </summary>
        public bool StartPendingRun(bool isMarvinMode)
        {
            if (!_isRunPending)
                return false;

            _isRunPending = false;
            Run = _isNewGame ? StartNewGame(isMarvinMode) : StartLoadedGame(_restoredRun, isMarvinMode);
            _restoredRun = null;
            return true;
        }

        /// <summary>
        /// The run for the save section. Null without a run.
        /// </summary>
        public JToken CaptureRun()
        {
            return Run == null ? null : JObject.FromObject(Run);
        }

        public AchievementRunState StartNewGame(bool isMarvinMode)
        {
            var run = new AchievementRunState { Tier = AchievementTier.Adanos };
            ApplyMarvinMode(run, isMarvinMode);
            return run;
        }

        /// <summary>
        /// A loaded save. restored = its section, null if it has none (older save): Adanos then.
        /// </summary>
        public AchievementRunState StartLoadedGame(AchievementRunState restored, bool isMarvinMode)
        {
            var run = restored ?? new AchievementRunState { Tier = AchievementTier.Adanos };
            ApplyMarvinMode(run, isMarvinMode);
            return run;
        }

        /// <summary>
        /// Turning Marvin mode on lowers the run to Beliar for good (also if turned off again).
        /// </summary>
        public void ApplyMarvinMode(AchievementRunState run, bool isMarvinMode)
        {
            if (isMarvinMode)
                run.LowerTierTo(AchievementTier.Beliar);
        }
    }
}
