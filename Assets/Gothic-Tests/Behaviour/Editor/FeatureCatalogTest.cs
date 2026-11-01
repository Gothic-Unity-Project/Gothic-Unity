using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Tests.Behaviour.Runner;
using NUnit.Framework;
using UnityEngine;

namespace Gothic.Tests.Behaviour.Editor
{
    /// <summary>
    /// Keeps the feature files and the step bindings in step without starting the game: every file parses, every step
    /// binds to exactly one [Step], every [Step] is used, every feature names its area, every @adr exists.
    /// </summary>
    public class FeatureCatalogTest
    {
        private static readonly string _adrFolder =
            Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Docs", "adr");

        private List<ScenarioCase> _scenarios;


        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _scenarios = FeatureCatalog.LoadAll();
        }

        [Test]
        public void ThereAreScenarios()
        {
            Assert.That(_scenarios, Is.Not.Empty, $"No scenarios below {FeatureCatalog.FeaturesFolder}");
        }

        [Test]
        public void FeatureFilesParse()
        {
            var errors = _scenarios.Where(scenario => scenario.ParseError != null)
                .Select(scenario => scenario.ParseError);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void EveryStepBindsToExactlyOneStepMethod()
        {
            var problems = _scenarios.SelectMany(scenario => scenario.Steps.Select(step => (scenario, step)))
                .Select(entry => (entry.scenario, entry.step, Matches: StepRegistry.FindMatches(entry.step.Text)))
                .Where(entry => entry.Matches.Count != 1)
                .Select(entry => $"{entry.scenario.FeatureFile}:{entry.step.Line} \"{entry.step.Text}\" - " +
                    (entry.Matches.Count == 0
                        ? "no binding"
                        : string.Join(", ", entry.Matches.Select(binding => binding.Describe()))))
                .Distinct();
            Assert.That(problems, Is.Empty);
        }

        [Test]
        public void EveryStepMethodIsUsed()
        {
            var texts = _scenarios.SelectMany(scenario => scenario.Steps).Select(step => step.Text).Distinct()
                .ToList();
            var unused = StepRegistry.Bindings
                .Where(binding => !binding.IsReserved && !texts.Any(binding.Matches))
                .Select(binding => binding.Describe());
            Assert.That(unused, Is.Empty, "Unused [Step] methods - use them in a feature, delete them or mark them " +
                "IsReserved with a reason");
        }

        [Test]
        public void EveryFeatureNamesItsArea()
        {
            var withoutArea = _scenarios.Where(scenario => scenario.ParseError == null && !scenario.HasTag("area"))
                .Select(scenario => scenario.FeatureFile).Distinct();
            Assert.That(withoutArea, Is.Empty, "Tag each feature with @area(<Core context>)");
        }

        [Test]
        public void EveryAdrTagNamesARecord()
        {
            var missing = _scenarios.Select(scenario => scenario.GetTagValue("adr")).Where(adr => adr != null)
                .Distinct()
                .Where(adr => !File.Exists(Path.Combine(_adrFolder, adr + ".md")));
            Assert.That(missing, Is.Empty, $"@adr(...) tags without a record in {_adrFolder}");
        }

        [Test]
        public void ScenarioNamesAreUnique()
        {
            var duplicates = _scenarios.GroupBy(scenario => scenario.TestName).Where(group => group.Count() > 1)
                .Select(group => group.Key);
            Assert.That(duplicates, Is.Empty);
        }
    }
}
