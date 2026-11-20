using Gothic.Core.Domain.Achievements;
using Gothic.Core.Models.Achievements;
using NUnit.Framework;

namespace Gothic.Tests.EditMode.Achievements
{
    /// <summary>
    /// Tier rules (Docs/achievements.md, "Tiers"): Innos needs no cheats and never going back.
    /// </summary>
    public class AchievementRunTest
    {
        private AchievementRunDomain _runDomain;
        private AchievementProfileData _profile;


        [SetUp]
        public void SetUp()
        {
            _runDomain = new AchievementRunDomain();
            _profile = new AchievementProfileData();
        }

        /// <summary>
        /// A run with saves 1..count committed; returns the state of the newest save.
        /// </summary>
        private AchievementRunState PlayAndSave(int count)
        {
            var run = _runDomain.StartNewGame(_profile, false);
            for (var i = 0; i < count; i++)
                _runDomain.CommitSave(_profile, run, _runDomain.GetNextSaveNo(_profile, run));
            return run;
        }

        private static AchievementRunState SaveOf(AchievementRunState run, int saveNo)
        {
            return new AchievementRunState { RunId = run.RunId, SaveNo = saveNo, Tier = run.Tier };
        }

        [Test]
        public void NewGameIsInnosWithANewKnownRun()
        {
            var run = _runDomain.StartNewGame(_profile, false);
            var other = _runDomain.StartNewGame(_profile, false);

            Assert.AreEqual(AchievementTier.Innos, run.Tier);
            Assert.AreNotEqual(run.RunId, other.RunId);
            Assert.IsTrue(_profile.Runs.ContainsKey(run.RunId));
        }

        [Test]
        public void NewGameInMarvinModeIsBeliar()
        {
            Assert.AreEqual(AchievementTier.Beliar, _runDomain.StartNewGame(_profile, true).Tier);
        }

        [Test]
        public void MarvinModeLowersToBeliarForGood()
        {
            var run = _runDomain.StartNewGame(_profile, false);

            _runDomain.ApplyMarvinMode(run, true);
            _runDomain.ApplyMarvinMode(run, false);

            Assert.AreEqual(AchievementTier.Beliar, run.Tier);
        }

        [Test]
        public void SaveNumbersRise()
        {
            var run = PlayAndSave(3);

            Assert.AreEqual(3, run.SaveNo);
            Assert.AreEqual(3, _profile.Runs[run.RunId].NewestSaveNo);
            Assert.AreEqual(4, _runDomain.GetNextSaveNo(_profile, run));
        }

        [Test]
        public void LoadingTheNewestSaveKeepsInnos()
        {
            var run = PlayAndSave(3);

            var loaded = _runDomain.StartLoadedGame(_profile, SaveOf(run, 3), true, false);

            Assert.AreEqual(AchievementTier.Innos, loaded.Tier);
        }

        [Test]
        public void LoadingAnOlderSaveIsAdanos()
        {
            var run = PlayAndSave(3);

            var loaded = _runDomain.StartLoadedGame(_profile, SaveOf(run, 2), true, false);

            Assert.AreEqual(AchievementTier.Adanos, loaded.Tier);
        }

        [Test]
        public void SavingAfterGoingBackGetsANumberAboveTheNewest()
        {
            var run = PlayAndSave(3);
            var loaded = _runDomain.StartLoadedGame(_profile, SaveOf(run, 1), true, false);

            Assert.AreEqual(4, _runDomain.GetNextSaveNo(_profile, loaded));
        }

        [Test]
        public void LoadingAfterTheHeroDiedIsAdanos()
        {
            var run = PlayAndSave(3);
            _runDomain.MarkSpent(_profile, run);

            var loaded = _runDomain.StartLoadedGame(_profile, SaveOf(run, 3), true, false);

            Assert.AreEqual(AchievementTier.Adanos, loaded.Tier);
        }

        [Test]
        public void SavingAgainClearsSpent()
        {
            var run = PlayAndSave(3);
            _runDomain.MarkSpent(_profile, run);

            _runDomain.CommitSave(_profile, run, _runDomain.GetNextSaveNo(_profile, run));

            Assert.IsFalse(_profile.Runs[run.RunId].IsSpent);
        }

        [Test]
        public void UnknownRunIsAdanos()
        {
            var foreign = new AchievementRunState { RunId = "foreign", SaveNo = 2, Tier = AchievementTier.Innos };

            var loaded = _runDomain.StartLoadedGame(_profile, foreign, true, false);

            Assert.AreEqual(AchievementTier.Adanos, loaded.Tier);
        }

        [Test]
        public void SaveNumberAboveTheProfilesNewestIsAdanos()
        {
            var run = PlayAndSave(2);

            var loaded = _runDomain.StartLoadedGame(_profile, SaveOf(run, 5), true, false);

            Assert.AreEqual(AchievementTier.Adanos, loaded.Tier);
        }

        [Test]
        public void SaveWithoutAchievementDataIsAdanosWithANewRun()
        {
            var loaded = _runDomain.StartLoadedGame(_profile, null, true, false);

            Assert.AreEqual(AchievementTier.Adanos, loaded.Tier);
            Assert.IsTrue(_profile.Runs.ContainsKey(loaded.RunId));
        }

        [Test]
        public void BrokenSealIsBeliar()
        {
            var run = PlayAndSave(1);

            var loaded = _runDomain.StartLoadedGame(_profile, SaveOf(run, 1), false, false);

            Assert.AreEqual(AchievementTier.Beliar, loaded.Tier);
        }

        [Test]
        public void TaintedSaveStaysBeliarAndLoadingInMarvinModeIsBeliar()
        {
            var run = PlayAndSave(2);
            var tainted = SaveOf(run, 2);
            tainted.Tier = AchievementTier.Beliar;

            Assert.AreEqual(AchievementTier.Beliar,
                _runDomain.StartLoadedGame(_profile, tainted, true, false).Tier);
            Assert.AreEqual(AchievementTier.Beliar,
                _runDomain.StartLoadedGame(_profile, SaveOf(run, 2), true, true).Tier);
        }

        [Test]
        public void CleanSaveOfTheSameRunGivesBackItsTier()
        {
            var run = PlayAndSave(2);
            var cleanSave = SaveOf(run, 2);
            _runDomain.ApplyMarvinMode(run, true);

            var loaded = _runDomain.StartLoadedGame(_profile, cleanSave, true, false);

            Assert.AreEqual(AchievementTier.Innos, loaded.Tier);
        }
    }
}
