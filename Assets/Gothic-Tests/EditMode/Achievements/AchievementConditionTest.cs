using System.Collections.Generic;
using Gothic.Core.Domain.Achievements;
using Gothic.Core.Models.Achievements;
using NUnit.Framework;

namespace Gothic.Tests.EditMode.Achievements
{
    /// <summary>
    /// Conditions read the fact first and the game state (here a fake) at the moment of the fact.
    /// </summary>
    public class AchievementConditionTest
    {
        private const int _kapitel = 10;
        private const int _gilSld = 7;
        private const int _meatbug = 30;
        private const int _scavenger = 31;
        private const int _infoA = 40;
        private const int _infoB = 41;
        private const int _kirgo = 50;
        private const int _aivarDefeated = 19;
        private const int _nugget = 60;

        private AchievementConditionDomain _conditionDomain;
        private FakeReader _reader;
        private AchievementRunState _run;
        private static readonly AchievementFact _tick = new(AchievementFactKind.VariableTick);


        private class FakeReader : IAchievementStateReader
        {
            public readonly Dictionary<int, int> Variables = new();
            public readonly Dictionary<string, int> Topics = new();
            public readonly HashSet<int> ToldInfos = new();
            public readonly Dictionary<int, int> Items = new();
            public readonly Dictionary<(int, int), int> Aivars = new();
            public int TrueGuild;

            public int GetVariable(int symbolIndex) => Variables.TryGetValue(symbolIndex, out var v) ? v : 0;
            public int GetLogTopicStatus(string topic) => Topics.TryGetValue(topic, out var v) ? v : -1;
            public bool IsInfoTold(int infoIndex) => ToldInfos.Contains(infoIndex);
            public int GetHeroTrueGuild() => TrueGuild;
            public int GetHeroItemCount(int itemIndex) => Items.TryGetValue(itemIndex, out var v) ? v : 0;
            public bool TryGetNpcAivar(int npc, int aivar, out int v) => Aivars.TryGetValue((npc, aivar), out v);
        }

        [SetUp]
        public void SetUp()
        {
            _conditionDomain = new AchievementConditionDomain();
            _reader = new FakeReader();
            _run = new AchievementRunState { RunId = "run", Tier = AchievementTier.Innos };
        }

        private static AchievementCondition Variable(int index, AchievementCompareOp op, int value)
        {
            return new AchievementCondition
            {
                Kind = AchievementConditionKind.Variable, ResolvedIndex = index, Op = op, Value = value
            };
        }

        private static AchievementCondition Group(AchievementConditionKind kind, params AchievementCondition[] children)
        {
            var condition = new AchievementCondition { Kind = kind };
            condition.Children.AddRange(children);
            return condition;
        }

        private bool IsMet(AchievementCondition condition, AchievementFact fact)
        {
            return _conditionDomain.IsMet(condition, fact, _reader, _run);
        }

        [Test]
        public void LogTopicComparesTheFactsTopicText()
        {
            var condition = new AchievementCondition
            {
                Kind = AchievementConditionKind.LogTopic, ResolvedText = "Aufnahme Altes Lager", Value = 2
            };

            Assert.IsTrue(IsMet(condition, new AchievementFact(AchievementFactKind.LogTopic,
                stringKey: "Aufnahme Altes Lager", value: 2)));
            Assert.IsFalse(IsMet(condition, new AchievementFact(AchievementFactKind.LogTopic,
                stringKey: "Aufnahme Altes Lager", value: 1)));
            Assert.IsFalse(IsMet(condition, _tick), "No such topic in the log yet.");
        }

        [Test]
        public void VariableComparesWithItsOp()
        {
            _reader.Variables[_kapitel] = 3;

            Assert.IsTrue(IsMet(Variable(_kapitel, AchievementCompareOp.GreaterOrEqual, 3), _tick));
            Assert.IsFalse(IsMet(Variable(_kapitel, AchievementCompareOp.Equal, 1), _tick));
            Assert.IsTrue(IsMet(Variable(_kapitel, AchievementCompareOp.NotEqual, 1), _tick));
        }

        [Test]
        public void HiredEarlyReadsTheChapterAtTheGuildFact()
        {
            var guild = new AchievementCondition { Kind = AchievementConditionKind.TrueGuild };
            guild.ResolvedIndices.Add(_gilSld);
            var hiredEarly = Group(AchievementConditionKind.All, guild,
                Variable(_kapitel, AchievementCompareOp.Equal, 1));
            var guildFact = new AchievementFact(AchievementFactKind.TrueGuild, value: _gilSld);

            _reader.Variables[_kapitel] = 1;
            Assert.IsTrue(IsMet(hiredEarly, guildFact));

            _reader.Variables[_kapitel] = 2;
            Assert.IsFalse(IsMet(hiredEarly, guildFact));
        }

        [Test]
        public void ToldInfosNeedTheDefeatFlags()
        {
            var aivar = new AchievementCondition
            {
                Kind = AchievementConditionKind.NpcAivar, ResolvedIndex = _kirgo, ResolvedAivarIndex = _aivarDefeated,
                Value = 1
            };
            var arena = Group(AchievementConditionKind.All,
                new AchievementCondition { Kind = AchievementConditionKind.InfoTold, ResolvedIndex = _infoA },
                new AchievementCondition { Kind = AchievementConditionKind.InfoTold, ResolvedIndex = _infoB },
                aivar);
            var fact = new AchievementFact(AchievementFactKind.InfoTold, _infoB);
            _reader.ToldInfos.Add(_infoA);
            _reader.ToldInfos.Add(_infoB);

            Assert.IsFalse(IsMet(arena, fact), "No such NPC in the world: false.");

            _reader.Aivars[(_kirgo, _aivarDefeated)] = 0;
            Assert.IsFalse(IsMet(arena, fact), "Told after a lost fight.");

            _reader.Aivars[(_kirgo, _aivarDefeated)] = 1;
            Assert.IsTrue(IsMet(arena, fact));
        }

        [Test]
        public void ItemCountsTheHerosItems()
        {
            var condition = new AchievementCondition
            {
                Kind = AchievementConditionKind.Item, ResolvedIndex = _nugget, Count = 15000
            };
            var fact = new AchievementFact(AchievementFactKind.HeroItem, _nugget);

            _reader.Items[_nugget] = 14999;
            Assert.IsFalse(IsMet(condition, fact));

            _reader.Items[_nugget] = 15000;
            Assert.IsTrue(IsMet(condition, fact));
            Assert.AreEqual(15000, _conditionDomain.GetProgress(condition, _reader, _run));
        }

        [Test]
        public void KillsCountOnlyMatchingVictimsByTheHero()
        {
            var condition = new AchievementCondition
            {
                Kind = AchievementConditionKind.Killed, Count = 2, IsByHero = true, KillCounterKey = "kill:pest:0"
            };
            condition.ResolvedIndices.Add(_meatbug);
            var byHero = new AchievementFact(AchievementFactKind.Kill, _meatbug, isByHero: true);

            _conditionDomain.CountKill(condition, new AchievementFact(AchievementFactKind.Kill, _meatbug), _run);
            _conditionDomain.CountKill(condition, new AchievementFact(AchievementFactKind.Kill, _scavenger,
                isByHero: true), _run);
            _conditionDomain.CountKill(condition, byHero, _run);
            Assert.IsFalse(IsMet(condition, byHero));

            _conditionDomain.CountKill(condition, byHero, _run);
            Assert.IsTrue(IsMet(condition, byHero));
        }

        [Test]
        public void CountersAndDistinctReadTheRun()
        {
            var counter = new AchievementCondition
            {
                Kind = AchievementConditionKind.Counter, Name = AchievementCounterIds.LockpickSuccess, Count = 2
            };
            var distinct = new AchievementCondition
            {
                Kind = AchievementConditionKind.Distinct, Name = AchievementCounterIds.DocumentShown, Count = 2
            };
            var fact = new AchievementFact(AchievementFactKind.Counter, stringKey: AchievementCounterIds.DocumentShown);

            _run.Increment(AchievementCounterIds.LockpickSuccess);
            _run.AddDistinct(AchievementCounterIds.DocumentShown, "ItWr_Book_01");
            Assert.IsFalse(_run.AddDistinct(AchievementCounterIds.DocumentShown, "ItWr_Book_01"));
            Assert.IsFalse(IsMet(counter, fact));
            Assert.IsFalse(IsMet(distinct, fact));

            _run.Increment(AchievementCounterIds.LockpickSuccess);
            _run.AddDistinct(AchievementCounterIds.DocumentShown, "ItWr_Letter_01");
            Assert.IsTrue(IsMet(counter, fact));
            Assert.IsTrue(IsMet(distinct, fact));
        }

        [Test]
        public void AnyNeedsOneAllNeedsEvery()
        {
            _reader.Variables[_kapitel] = 2;
            var yes = Variable(_kapitel, AchievementCompareOp.Equal, 2);
            var no = Variable(_kapitel, AchievementCompareOp.Equal, 5);

            Assert.IsTrue(IsMet(Group(AchievementConditionKind.Any, no, yes), _tick));
            Assert.IsFalse(IsMet(Group(AchievementConditionKind.All, no, yes), _tick));
            Assert.IsTrue(Group(AchievementConditionKind.All, no, yes).IsPureVariable);
        }
    }
}
