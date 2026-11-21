using Gothic.Core.Domain.Achievements;
using Gothic.Core.Models.Achievements;
using NUnit.Framework;

namespace Gothic.Tests.EditMode.Achievements
{
    /// <summary>
    /// Tier rules (Docs/achievements.md, "Tiers"): Adanos is normal play, Marvin mode makes a run Beliar for good.
    /// </summary>
    public class AchievementRunTest
    {
        private AchievementRunDomain _runDomain;


        [SetUp]
        public void SetUp()
        {
            _runDomain = new AchievementRunDomain();
        }

        [Test]
        public void NewGameIsAdanos()
        {
            Assert.AreEqual(AchievementTier.Adanos, _runDomain.StartNewGame(false).Tier);
        }

        [Test]
        public void NewGameInMarvinModeIsBeliar()
        {
            Assert.AreEqual(AchievementTier.Beliar, _runDomain.StartNewGame(true).Tier);
        }

        [Test]
        public void MarvinModeLowersToBeliarForGood()
        {
            var run = _runDomain.StartNewGame(false);

            _runDomain.ApplyMarvinMode(run, true);
            _runDomain.ApplyMarvinMode(run, false);

            Assert.AreEqual(AchievementTier.Beliar, run.Tier);
        }

        [Test]
        public void OlderSaveWithoutSectionIsAdanos()
        {
            Assert.AreEqual(AchievementTier.Adanos, _runDomain.StartLoadedGame(null, false).Tier);
        }

        [Test]
        public void TaintedSaveStaysBeliarAndLoadingInMarvinModeIsBeliar()
        {
            var tainted = new AchievementRunState { Tier = AchievementTier.Beliar };
            var clean = new AchievementRunState { Tier = AchievementTier.Adanos };

            Assert.AreEqual(AchievementTier.Beliar, _runDomain.StartLoadedGame(tainted, false).Tier);
            Assert.AreEqual(AchievementTier.Beliar, _runDomain.StartLoadedGame(clean, true).Tier);
        }

        [Test]
        public void CleanSaveGivesBackAdanosAfterCheating()
        {
            _runDomain.ResetRun(true);
            _runDomain.BeginLoad();
            _runDomain.StartPendingRun(false);
            var cleanSave = _runDomain.CaptureRun();
            _runDomain.ApplyMarvinMode(_runDomain.Run, true);

            _runDomain.ResetRun(false);
            _runDomain.BeginLoad();
            Assert.IsTrue(_runDomain.RestoreRun(cleanSave));
            _runDomain.StartPendingRun(false);

            Assert.AreEqual(AchievementTier.Adanos, _runDomain.Run.Tier);
        }

        [Test]
        public void SaveSectionKeepsCounters()
        {
            _runDomain.ResetRun(true);
            _runDomain.BeginLoad();
            _runDomain.StartPendingRun(false);
            _runDomain.Run.Increment("lockpick.success");
            _runDomain.Run.AddDistinct("document.shown", "ItWr_Book_Circle_01");
            var section = _runDomain.CaptureRun();

            _runDomain.ResetRun(false);
            _runDomain.BeginLoad();
            _runDomain.RestoreRun(section);
            _runDomain.StartPendingRun(false);

            Assert.AreEqual(1, _runDomain.Run.GetCounter("lockpick.success"));
            Assert.AreEqual(1, _runDomain.Run.GetDistinctCount("document.shown"));
        }

        [Test]
        public void LevelChangeKeepsTheRun()
        {
            _runDomain.ResetRun(true);
            _runDomain.BeginLoad();
            _runDomain.StartPendingRun(false);
            var run = _runDomain.Run;

            Assert.IsFalse(_runDomain.StartPendingRun(false));
            Assert.AreSame(run, _runDomain.Run);
        }

        [Test]
        public void BetterTierReplacesTheUnlock()
        {
            var profile = new AchievementProfileData();
            profile.SetUnlock("g1.join_old_camp", AchievementTier.Beliar);

            Assert.IsTrue(profile.IsEarned("g1.join_old_camp", AchievementTier.Beliar));
            Assert.IsFalse(profile.IsEarned("g1.join_old_camp", AchievementTier.Adanos));

            profile.SetUnlock("g1.join_old_camp", AchievementTier.Adanos);

            Assert.AreEqual(1, profile.Unlocks.Count);
            Assert.AreEqual(AchievementTier.Adanos, profile.Unlocks[0].Tier);
        }

        [Test]
        public void ProfileIsPlainJson()
        {
            var profile = new AchievementProfileData();
            profile.SetUnlock("g1.join_old_camp", AchievementTier.Adanos);

            var reopened = AchievementProfileDomain.Open(AchievementProfileDomain.Serialize(profile));

            Assert.AreEqual("g1.join_old_camp", reopened.Unlocks[0].Id);
            Assert.AreEqual(AchievementTier.Adanos, reopened.Unlocks[0].Tier);
        }
    }
}
