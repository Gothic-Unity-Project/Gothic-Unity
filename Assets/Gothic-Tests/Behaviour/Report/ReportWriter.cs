using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Gothic.Tests.Behaviour.Runner;

namespace Gothic.Tests.Behaviour.Report
{
    /// <summary>
    /// report.md of a run: a table per feature (status, time, memory, warnings) and, for every scenario that didn't
    /// simply pass, why - failing step, message, screenshots, the last warnings and errors. Known bugs are listed
    /// apart: a failure is expected, a pass means the bug is gone.
    /// </summary>
    public static class ReportWriter
    {
        // From Logs/BehaviourTests/<run>/ to the repository.
        private const string _repositoryRoot = "../../../";
        private const int _warningsShown = 10;

        public static string Build(string runName, IReadOnlyList<ScenarioResult> results,
            IReadOnlyList<ScenarioCase> catalog)
        {
            var text = new StringBuilder();
            text.AppendLine($"# Behaviour scenarios, run {runName}");
            text.AppendLine();
            AppendSummary(text, results);
            AppendNeedsALook(text, results, catalog);

            foreach (var feature in results.GroupBy(result => result.FeatureFile))
                AppendFeature(text, feature.ToList());

            return text.ToString();
        }

        private static void AppendSummary(StringBuilder text, IReadOnlyList<ScenarioResult> results)
        {
            if (results.Count == 0)
            {
                text.AppendLine("No scenario ran.");
                return;
            }

            var versions = string.Join(", ", results.Select(result => result.GameVersion).Distinct());
            var worlds = string.Join(", ", results.Select(result => result.World).Where(world => world != null)
                .Distinct());
            text.AppendLine($"{versions}, {worlds}. {results.Count} scenarios in " +
                $"{Seconds(results.Sum(result => result.Seconds))}, of it {Seconds(results.Sum(r => r.LoadSeconds))} " +
                $"loading the game. Started {results[0].StartedAt}.");
            text.AppendLine();
            text.AppendLine("| Feature | Passed | Failed | Known bug | Inconclusive | Skipped | Time |");
            text.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (var feature in results.GroupBy(result => result.FeatureFile))
                AppendCountRow(text, Topic(feature.Key), feature.ToList());
            AppendCountRow(text, "**All**", results);
            text.AppendLine();
            text.AppendLine("Known bug: tagged @known-bug and failing as expected. Inconclusive: no verdict (the " +
                "message says what was missing); the editor's test totals don't count these.");
            text.AppendLine();
        }

        private static void AppendCountRow(StringBuilder text, string name, IReadOnlyList<ScenarioResult> results)
        {
            int Count(string status) => results.Count(result => result.Status == status);

            var failed = Count(ScenarioResult.Failed) + Count(ScenarioResult.Aborted);
            text.AppendLine($"| {name} | {Count(ScenarioResult.Passed)} | {failed} | " +
                $"{Count(ScenarioResult.KnownBug)} | {Count(ScenarioResult.Inconclusive)} | " +
                $"{Count(ScenarioResult.Skipped)} | " +
                $"{Seconds(results.Sum(result => result.Seconds))} |");
        }

        private static void AppendNeedsALook(StringBuilder text, IReadOnlyList<ScenarioResult> results,
            IReadOnlyList<ScenarioCase> catalog)
        {
            text.AppendLine("## Needs a look");
            text.AppendLine();
            AppendList(text, "Failed - a rule is broken or a scenario is", results.Where(result =>
                result.Status is ScenarioResult.Failed or ScenarioResult.Aborted), true);
            AppendList(text, "Known bugs that are gone - the scenario passes: remove its @known-bug and close the bug",
                results.Where(result => result.IsKnownBug && result.Status == ScenarioResult.Passed), false);
            AppendList(text, "Inconclusive - no verdict", results.Where(result =>
                result.Status == ScenarioResult.Inconclusive), true);
            AppendList(text, "Known bugs still there - expected failures", results.Where(result =>
                result.Status == ScenarioResult.KnownBug), true);
            AppendList(text, "Skipped", results.Where(result => result.Status == ScenarioResult.Skipped), true);

            var ran = results.Select(result => result.Test).ToHashSet();
            var features = results.Select(result => result.FeatureFile).ToHashSet();
            var notRun = catalog.Where(scenario => features.Contains(scenario.FeatureFile) &&
                                                   !ran.Contains(scenario.TestName)).ToList();
            if (notRun.Count > 0)
            {
                var manual = notRun.Count(scenario => scenario.HasTag("manual"));
                text.AppendLine($"Not in this run: {notRun.Count} more scenarios of these features ({manual} " +
                    $"@manual, {notRun.Count - manual} filtered out by the run). The manual ones and those skipped " +
                    "for their tag are in checklist.md.");
                text.AppendLine();
            }
        }

        private static void AppendList(StringBuilder text, string title, IEnumerable<ScenarioResult> results,
            bool withMessage)
        {
            var list = results.ToList();
            if (list.Count == 0)
                return;

            text.AppendLine($"**{title}** ({list.Count}):");
            text.AppendLine();
            foreach (var result in list)
            {
                var message = result.FailingStep != null ? $"{result.FailingStep}: {result.Message}" : result.Message;
                var why = withMessage && !string.IsNullOrEmpty(result.Message)
                    ? $" - {OneLine(message)}"
                    : string.Empty;
                text.AppendLine($"- {result.Test}{why}");
            }

            text.AppendLine();
        }

        private static void AppendFeature(StringBuilder text, IReadOnlyList<ScenarioResult> results)
        {
            var first = results[0];
            text.AppendLine($"## {first.FeatureFile}");
            text.AppendLine();
            var adr = GetTagValue(first.Tags, "adr");
            text.AppendLine(adr != null
                ? $"{first.Feature}. Decision record: [{adr}]({_repositoryRoot}Docs/adr/{adr}.md)"
                : $"{first.Feature}.");
            text.AppendLine();
            text.AppendLine("| Scenario | Status | Time | Memory | Warnings / errors |");
            text.AppendLine("|---|---|---:|---:|---:|");
            foreach (var result in results)
            {
                text.AppendLine($"| {Cell(result.Scenario)} | {Describe(result)} | {Seconds(result.Seconds)} | " +
                    $"{result.MemoryDeltaMb.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)} MB | " +
                    $"{result.WarningCount} / {result.ErrorCount} |");
            }

            text.AppendLine();
            foreach (var result in results.Where(result => result.Status != ScenarioResult.Passed ||
                                                           result.Images.Length > 0))
                AppendDetails(text, result);
        }

        private static void AppendDetails(StringBuilder text, ScenarioResult result)
        {
            text.AppendLine($"### {result.Scenario} - {Describe(result)}");
            text.AppendLine();
            text.AppendLine($"Line {result.Line}, started {result.StartedAt}, tags " +
                $"{string.Join(" ", result.Tags.Select(tag => "@" + tag))}.");
            text.AppendLine();
            if (result.FailingStep != null)
            {
                text.AppendLine($"Step: `{result.FailingStep}`");
                text.AppendLine();
            }

            if (!string.IsNullOrEmpty(result.Message))
            {
                text.AppendLine("```text");
                text.AppendLine(result.Message.Trim());
                text.AppendLine("```");
                text.AppendLine();
            }

            foreach (var image in result.Images)
                text.AppendLine($"![{image}]({image.Replace(" ", "%20")})");
            if (result.Images.Length > 0)
                text.AppendLine();

            if (result.LastWarnings.Length == 0 || result.Status == ScenarioResult.Passed)
                return;

            var shown = result.LastWarnings.Reverse().Take(_warningsShown).Reverse().ToList();
            text.AppendLine($"<details><summary>Last {shown.Count} of {result.WarningCount + result.ErrorCount} " +
                "warnings and errors</summary>");
            text.AppendLine();
            text.AppendLine("```text");
            foreach (var line in shown)
                text.AppendLine(OneLine(line));
            text.AppendLine("```");
            text.AppendLine();
            text.AppendLine("</details>");
            text.AppendLine();
        }

        private static string Describe(ScenarioResult result)
        {
            return result.Status switch
            {
                ScenarioResult.Passed when result.IsKnownBug => "passed - known bug gone?",
                ScenarioResult.Passed => "passed",
                ScenarioResult.Failed => "FAILED",
                ScenarioResult.KnownBug => "known bug, still there",
                ScenarioResult.Inconclusive => "inconclusive",
                ScenarioResult.Skipped => "skipped",
                ScenarioResult.Aborted => "ABORTED",
                _ => result.Status
            };
        }

        private static string Topic(string featureFile)
        {
            var name = featureFile[(featureFile.LastIndexOf('/') + 1)..];
            return name.EndsWith(".feature") ? name[..^".feature".Length] : name;
        }

        private static string GetTagValue(IEnumerable<string> tags, string name)
        {
            var prefix = name + "(";
            var tag = tags.FirstOrDefault(t => t.StartsWith(prefix) && t.EndsWith(")"));
            return tag?.Substring(prefix.Length, tag.Length - prefix.Length - 1);
        }

        private static string Seconds(float seconds)
        {
            return seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        }

        private static string OneLine(string text)
        {
            return string.Join(" / ", text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
        }

        private static string Cell(string text)
        {
            return OneLine(text).Replace("|", "\\|");
        }
    }
}
