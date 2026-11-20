using System;
using Gothic.Core.Models.Achievements;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// The tier rules of a run (Docs/achievements.md, "Tiers"): a new game is Innos, going back to an older save or
    /// loading after the hero died is Adanos, Marvin mode is Beliar. The profile decides when save and profile
    /// disagree. Pure: the Marvin state is passed in (EditMode-testable).
    /// </summary>
    public class AchievementRunDomain
    {
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
