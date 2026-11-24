using System.Collections.Generic;
using Gothic.Core.Domain.Achievements;
using Gothic.Core.Models.Achievements;
using NUnit.Framework;

namespace Gothic.Tests.EditMode.Achievements
{
    /// <summary>
    /// Pure variable conditions are checked after script moments, never by polling (Docs/achievements.md): one check
    /// per frame, only values changed since the snapshot unlock, a level change keeps the snapshot.
    /// </summary>
    public class AchievementVariableWatchTest
    {
        private const int _kapitel = 10;
        private const int _enteredAddonWorld = 11;

        private AchievementVariableWatchDomain _watch;
        private FakeReader _reader;
        private AchievementProfileData _profile;
        private AchievementRunState _run;
        private List<AchievementDefinition> _definitions;
        private List<AchievementDefinition> _met;


        private class FakeReader : IAchievementStateReader
        {
            public readonly Dictionary<int, int> Variables = new();

            public int GetVariable(int symbolIndex) => Variables.TryGetValue(symbolIndex, out var v) ? v : 0;
            public int GetLogTopicStatus(string topic) => -1;
            public bool IsInfoTold(int infoIndex) => false;
            public int GetHeroTrueGuild() => 0;
            public int GetHeroItemCount(int itemIndex) => 0;
            public bool TryGetNpcAivar(int npc, int aivar, out int v)
            {
                v = 0;
                return false;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _watch = new AchievementVariableWatchDomain();
            _reader = new FakeReader();
            _run = new AchievementRunState { Tier = AchievementTier.Adanos };
            _profile = new AchievementProfileData();
            _met = new List<AchievementDefinition>();
            _definitions = new List<AchievementDefinition>
            {
                Definition("g2.cast_off", _kapitel, AchievementCompareOp.GreaterOrEqual, 6),
                Definition("g2.beyond_the_portal", _enteredAddonWorld, AchievementCompareOp.Equal, 1)
            };
        }

        private static AchievementDefinition Definition(string id, int variable, AchievementCompareOp op, int value)
        {
            return new AchievementDefinition
            {
                Id = id,
                Condition = new AchievementCondition
                {
                    Kind = AchievementConditionKind.Variable, ResolvedIndex = variable, Op = op, Value = value
                }
            };
        }

        private List<string> Check()
        {
            _met.Clear();
            _watch.Check(_definitions, _reader, _run, _profile, _met);
            return _met.ConvertAll(i => i.Id);
        }

        [Test]
        public void SeveralMomentsInOneFrameScheduleOneCheck()
        {
            _watch.TakeSnapshot(_definitions, _reader);

            Assert.IsTrue(_watch.MarkDirty(), "The first moment schedules the check.");
            Assert.IsFalse(_watch.MarkDirty(), "The dialog's item in the same frame: no second check.");
            Assert.IsFalse(_watch.MarkDirty());
            Assert.IsTrue(_watch.IsDirty);

            _reader.Variables[_kapitel] = 6;
            CollectionAssert.AreEqual(new[] { "g2.cast_off" }, Check());
            Assert.IsFalse(_watch.IsDirty, "The check clears the mark.");
            Assert.IsTrue(_watch.MarkDirty(), "The next moment schedules again.");
        }

        [Test]
        public void NothingIsDirtyWithoutAMoment()
        {
            _watch.TakeSnapshot(_definitions, _reader);

            Assert.IsFalse(_watch.IsDirty, "No moment, no check: zero cost per frame.");

            _watch.MarkDirty();
            _watch.ClearDirty();
            Assert.IsFalse(_watch.IsDirty, "Leaving the world forgets a pending check.");
        }

        [Test]
        public void LoadedValuesUnlockNothing()
        {
            // The save's world is ready: Kapitel 6 and the portal flag come from SAVEDAT.SAV.
            _reader.Variables[_kapitel] = 6;
            _reader.Variables[_enteredAddonWorld] = 1;
            _watch.TakeSnapshot(_definitions, _reader);

            _watch.MarkDirty();
            CollectionAssert.IsEmpty(Check(), "No retroactive unlocks (ADR decision 8).");

            _reader.Variables[_kapitel] = 7;
            CollectionAssert.AreEqual(new[] { "g2.cast_off" }, Check(), "A change during play counts.");
        }

        [Test]
        public void ValueSetByTheNewWorldsInitUnlocksAtWorldReady()
        {
            // Snapshot in Khorinis, then a level change: the new world's INIT_/STARTUP_ scripts set the flag.
            _watch.TakeSnapshot(_definitions, _reader);
            _reader.Variables[_enteredAddonWorld] = 1;

            // No new snapshot for a level change - the check at world ready compares with the old one.
            CollectionAssert.AreEqual(new[] { "g2.beyond_the_portal" }, Check());
        }

        [Test]
        public void FallbackCheckWorksWithoutAMoment()
        {
            _watch.TakeSnapshot(_definitions, _reader);
            _reader.Variables[_kapitel] = 6; // Set by a script moment we don't hook.

            Assert.IsFalse(_watch.IsDirty);
            CollectionAssert.AreEqual(new[] { "g2.cast_off" }, Check(), "RecheckNow (help panel, page, save).");
        }

        [Test]
        public void EarnedInThisTierIsSkipped()
        {
            _watch.TakeSnapshot(_definitions, _reader);
            _profile.SetUnlock("g2.cast_off", AchievementTier.Adanos);
            _reader.Variables[_kapitel] = 6;

            CollectionAssert.IsEmpty(Check());
        }

        [Test]
        public void EarnedInAWorseTierIsCheckedAgain()
        {
            _watch.TakeSnapshot(_definitions, _reader);
            _profile.SetUnlock("g2.cast_off", AchievementTier.Beliar);
            _reader.Variables[_kapitel] = 6;

            CollectionAssert.AreEqual(new[] { "g2.cast_off" }, Check(), "Upgrade Beliar -> Adanos.");
        }
    }
}
