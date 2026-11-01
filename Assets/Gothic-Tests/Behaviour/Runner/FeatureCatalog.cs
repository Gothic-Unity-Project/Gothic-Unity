using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Gherkin;
using Gherkin.CucumberMessages;
using Gherkin.CucumberMessages.Pickles;
using Io.Cucumber.Messages.Types;
using NUnit.Framework;
using UnityEngine;
using Exception = System.Exception;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// The scenarios of all feature files below Features/, parsed with the official Gherkin parser. Outlines become one
    /// scenario per example row, the background's steps come first. Order: by world (one world load each), the slow
    /// and world-reloading ones (@slow, @fresh-world) last, then as written.
    /// </summary>
    public static class FeatureCatalog
    {
        public static readonly string FeaturesFolder =
            Path.Combine(Application.dataPath, "Gothic-Tests", "Behaviour", "Features");

        // The step that loads a world (WorldSteps) - scenarios are sorted by it.
        private static readonly Regex _worldStep = new("^the world \"(.*)\" is loaded$");

        /// <summary>
        /// TestCaseSource of ScenarioRunner: one test per scenario, named "&lt;feature file&gt; / &lt;scenario&gt;", with a
        /// category per tag (see ToCategory).
        /// </summary>
        public static IEnumerable<TestCaseData> Scenarios()
        {
            foreach (var scenario in LoadAll())
            {
                // Returns(null): NUnit refuses a test method that returns something without an expected result. The
                // Test Framework runs the returned IEnumerator as a coroutine and never compares the result.
                var data = new TestCaseData(scenario).Returns(null).SetName(scenario.TestName);
                foreach (var tag in scenario.Tags)
                    data.SetCategory(ToCategory(tag));

                if (scenario.HasTag("manual"))
                    data.Ignore("@manual - on the headset checklist");
                else if (scenario.HasTag("flat") && !scenario.HasTag("vr"))
                    data.Ignore("@flat - the Flat module doesn't run yet");

                yield return data;
            }
        }

        /// <summary>
        /// The Test Runner filters categories as regular expressions, so a tag with a value becomes "name:value"
        /// (@area(world): "area:world") - parentheses would be read as a group.
        /// </summary>
        public static string ToCategory(string tag)
        {
            var open = tag.IndexOf('(');
            return open > 0 && tag.EndsWith(")") ? $"{tag[..open]}:{tag[(open + 1)..^1]}" : tag;
        }

        public static List<ScenarioCase> LoadAll()
        {
            if (!Directory.Exists(FeaturesFolder))
                return new List<ScenarioCase>();

            var scenarios = Directory.GetFiles(FeaturesFolder, "*.feature", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .SelectMany(path => Parse(Path.GetRelativePath(FeaturesFolder, path).Replace('\\', '/'),
                    File.ReadAllText(path)))
                .ToList();

            var worldOrder = scenarios.Select(s => s.World ?? string.Empty).Distinct().ToList();
            return scenarios
                .Select((scenario, index) => (scenario, index))
                .OrderBy(entry => worldOrder.IndexOf(entry.scenario.World ?? string.Empty))
                .ThenBy(entry => entry.scenario.HasTag("slow") || entry.scenario.HasTag("fresh-world"))
                .ThenBy(entry => entry.index)
                .Select(entry => entry.scenario)
                .ToList();
        }

        /// <summary>
        /// The scenarios of one feature file. A file that doesn't parse yields one scenario that fails with the
        /// parser's message, so the run shows it instead of losing the file.
        /// </summary>
        public static List<ScenarioCase> Parse(string featureFile, string text)
        {
            try
            {
                var idGenerator = new IncrementingIdGenerator();
                var ast = new Parser().Parse(new StringReader(text));
                var document = new AstMessagesConverter(idGenerator).ConvertGherkinDocumentToEventArgs(ast, featureFile);
                var pickles = new PickleCompiler(idGenerator).Compile(document);
                return ToScenarios(featureFile, document, pickles);
            }
            catch (Exception e)
            {
                return new List<ScenarioCase> { new(featureFile, $"{featureFile}: {e.Message}") };
            }
        }

        private static List<ScenarioCase> ToScenarios(string featureFile, GherkinDocument document,
            List<Pickle> pickles)
        {
            var steps = new Dictionary<string, ScenarioStep>();
            var rows = new Dictionary<string, (TableRow Header, TableRow Row)>();
            foreach (var child in document.Feature?.Children ?? new List<FeatureChild>())
            {
                AddNodes(child.Background, child.Scenario, steps, rows);
                foreach (var ruleChild in child.Rule?.Children ?? new List<RuleChild>())
                    AddNodes(ruleChild.Background, ruleChild.Scenario, steps, rows);
            }

            return pickles.Select(pickle =>
            {
                var scenarioSteps = pickle.Steps
                    .Select(pickleStep =>
                    {
                        var source = steps[pickleStep.AstNodeIds[0]];
                        return new ScenarioStep(source.Keyword, pickleStep.Text, source.Line, source.IsBackground);
                    })
                    .ToList();
                var tags = pickle.Tags.Select(tag => tag.Name.TrimStart('@')).ToList();
                var world = scenarioSteps.Select(step => _worldStep.Match(step.Text))
                    .FirstOrDefault(match => match.Success)?.Groups[1].Value;
                return new ScenarioCase(featureFile, document.Feature!.Name, GetName(pickle, rows),
                    (int)pickle.Location.Line, tags, scenarioSteps, world);
            }).ToList();
        }

        private static void AddNodes(Background background, Scenario scenario, Dictionary<string, ScenarioStep> steps,
            Dictionary<string, (TableRow Header, TableRow Row)> rows)
        {
            foreach (var step in background?.Steps ?? new List<Step>())
                steps[step.Id] = new ScenarioStep(step.Keyword, step.Text, (int)step.Location.Line, true);

            if (scenario == null)
                return;

            foreach (var step in scenario.Steps)
                steps[step.Id] = new ScenarioStep(step.Keyword, step.Text, (int)step.Location.Line, false);
            foreach (var examples in scenario.Examples.Where(examples => examples.TableHeader != null))
            {
                foreach (var row in examples.TableBody)
                    rows[row.Id] = (examples.TableHeader, row);
            }
        }

        /// <summary>
        /// An outline's rows share the scenario's name, so each gets its values appended: "... - distance 7.5,
        /// result open". No brackets: the Test Runner filters names as regular expressions too.
        /// </summary>
        private static string GetName(Pickle pickle, Dictionary<string, (TableRow Header, TableRow Row)> rows)
        {
            if (pickle.AstNodeIds.Count < 2 || !rows.TryGetValue(pickle.AstNodeIds[1], out var example))
                return pickle.Name;

            var values = example.Header.Cells.Zip(example.Row.Cells, (header, cell) => $"{header.Value} {cell.Value}");
            return $"{pickle.Name} - {string.Join(", ", values)}";
        }
    }
}
