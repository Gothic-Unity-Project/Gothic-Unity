using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Domain.Achievements;
using Gothic.Core.Models.Achievements;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Gothic.Tests.EditMode.Achievements
{
    /// <summary>
    /// The shipped definition files parse completely, without the scripts (symbols are resolved at runtime).
    /// </summary>
    public class AchievementDefinitionTest
    {
        private static string ReadDefinitionFile(string fileName)
        {
            return File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Achievements", fileName));
        }

        [Test]
        public void G1FileParsesCompletely()
        {
            var json = ReadDefinitionFile("g1.json");
            var errors = new List<string>();

            var definitions = AchievementDefinitionDomain.Parse(json, errors);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(((JArray)JObject.Parse(json)["achievements"]).Count, definitions.Count);
            Assert.Greater(definitions.Count, 0);
        }

        [Test]
        public void G1IdsAreUniqueAndPrefixed()
        {
            var definitions = AchievementDefinitionDomain.Parse(ReadDefinitionFile("g1.json"), new List<string>());

            var ids = definitions.Select(i => i.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
            Assert.IsTrue(ids.All(i => i.StartsWith("g1.")), "Every G1 id starts with g1.");
        }

        [Test]
        public void G1HasEnglishAndGermanTexts()
        {
            var definitions = AchievementDefinitionDomain.Parse(ReadDefinitionFile("g1.json"), new List<string>());

            foreach (var definition in definitions)
            {
                foreach (var language in new[] { "en", "de" })
                {
                    Assert.IsTrue(definition.Title.Translations.ContainsKey(language), $"{definition.Id} {language}");
                    Assert.IsTrue(definition.Description.Translations.ContainsKey(language),
                        $"{definition.Id} {language}");
                }
            }
        }

        [Test]
        public void G2FileIsValid()
        {
            var errors = new List<string>();

            AchievementDefinitionDomain.Parse(ReadDefinitionFile("g2.json"), errors);

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void EveryConditionKindParses()
        {
            const string json = @"{ ""achievements"": [
                { ""id"": ""a"", ""title"": { ""en"": ""A"" }, ""description"": { ""en"": ""A"" },
                  ""condition"": { ""all"": [
                    { ""logTopic"": ""CH1_JoinOC"", ""status"": ""success"" },
                    { ""variable"": ""Kapitel"", ""op"": "">="", ""value"": 3 },
                    { ""variable"": ""Jesse_PayForMe"", ""op"": ""=="", ""value"": ""LOG_SUCCESS"" },
                    { ""trueGuild"": [""GIL_SLD""] },
                    { ""infoTold"": ""DIA_Gilbert_First"" },
                    { ""item"": ""ItMiNugget"", ""count"": 15000 },
                    { ""killed"": [""Meatbug""], ""count"": 10, ""byHero"": true },
                    { ""npcAivar"": ""GRD_251_Kirgo"", ""aivar"": ""AIV_WASDEFEATEDBYSC"", ""value"": 1 },
                    { ""counter"": ""lockpick.success"", ""count"": 20 },
                    { ""any"": [ { ""distinct"": ""document.shown"", ""count"": 10 } ] }
                  ] } } ] }";
            var errors = new List<string>();

            var condition = AchievementDefinitionDomain.Parse(json, errors).Single().Condition;

            Assert.IsEmpty(errors, string.Join("\n", errors));
            var kinds = condition.Children.Select(i => i.Kind).ToList();
            CollectionAssert.AreEqual(new[]
            {
                AchievementConditionKind.LogTopic, AchievementConditionKind.Variable,
                AchievementConditionKind.Variable, AchievementConditionKind.TrueGuild,
                AchievementConditionKind.InfoTold, AchievementConditionKind.Item, AchievementConditionKind.Killed,
                AchievementConditionKind.NpcAivar, AchievementConditionKind.Counter, AchievementConditionKind.Any
            }, kinds);
            Assert.AreEqual("LOG_SUCCESS", condition.Children[2].ValueName);
            Assert.AreEqual(AchievementCompareOp.GreaterOrEqual, condition.Children[1].Op);
            Assert.IsTrue(condition.Children[6].IsByHero);
            Assert.AreEqual(10, condition.Children[6].Count);
            Assert.AreEqual("kill:a:0", condition.Children[6].KillCounterKey);
        }

        [Test]
        public void BrokenDefinitionIsSkippedAndNamed()
        {
            const string json = @"{ ""achievements"": [
                { ""id"": ""ok"", ""title"": { ""en"": ""A"" }, ""description"": { ""en"": ""A"" },
                  ""condition"": { ""counter"": ""x"" } },
                { ""id"": ""broken"", ""title"": { ""en"": ""B"" }, ""description"": { ""en"": ""B"" },
                  ""condition"": { ""unknownKind"": 1 } },
                { ""id"": ""ok"", ""title"": { ""en"": ""C"" }, ""description"": { ""en"": ""C"" },
                  ""condition"": { ""counter"": ""y"" } } ] }";
            var errors = new List<string>();

            var definitions = AchievementDefinitionDomain.Parse(json, errors);

            Assert.AreEqual(1, definitions.Count);
            Assert.AreEqual(2, errors.Count);
            StringAssert.StartsWith("broken", errors[0]);
        }

        [Test]
        public void OnlyPureVariableDefinitionsGoToTheVariableCheck()
        {
            var definitions = AchievementDefinitionDomain.Parse(ReadDefinitionFile("g1.json"), new List<string>());

            var index = AchievementDefinitionDomain.BuildIndex(definitions);

            var variableIds = index.VariableDefinitions.Select(i => i.Id).ToList();
            CollectionAssert.Contains(variableIds, "g1.great_invocation");
            CollectionAssert.Contains(variableIds, "g1.debts_settled");
            CollectionAssert.DoesNotContain(variableIds, "g1.hired_early");
            var guildFact = new AchievementFact(AchievementFactKind.TrueGuild, value: 7);
            CollectionAssert.Contains(index.Get(guildFact).Select(i => i.Id).ToList(), "g1.hired_early");
        }
    }
}
