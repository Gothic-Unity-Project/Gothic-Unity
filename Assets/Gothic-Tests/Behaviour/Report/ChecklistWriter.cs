using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Gothic.Tests.Behaviour.Runner;
using ZenKit;

namespace Gothic.Tests.Behaviour.Report
{
    /// <summary>
    /// checklist.md: what a person checks in the headset - every @manual scenario and every scenario the run skips for
    /// its tag (@input without an input session, the other game version, @flat). Grouped by feature, as Given/When/Then
    /// instructions with the place, time and switches to set up.
    /// </summary>
    public static class ChecklistWriter
    {
        // Step texts the set-up line is read from (WorldSteps, SettingsSteps).
        private static readonly Regex _wayPointStep = new("^the hero stands at waypoint \"(.*)\"$");
        private static readonly Regex _timeStep = new("^it is (\\d{1,2}):(\\d{2})$");
        private static readonly Regex _worldStep = new("^the world \"(.*)\" is loaded$");
        private static readonly Regex _switchStep = new("^the developer switch \"(.*)\" is (on|off)$");
        private static readonly Regex _iniStep = new("^the Gothic.ini option \"(.*)\" is (.*)$");

        /// <param name="title">What the list is for ("run 2026-11-01-2130").</param>
        /// <param name="scenarios">The scenarios to pick from (the catalog, or the features of a run).</param>
        /// <param name="version">The run's game version: the other version's scenarios are on the list.</param>
        /// <param name="hasInputSession">The @input scenarios ran - they're not on the list.</param>
        public static string Build(string title, IEnumerable<ScenarioCase> scenarios, GameVersion version,
            bool hasInputSession)
        {
            var picked = scenarios.Where(scenario => scenario.ParseError == null)
                .Select(scenario => (Scenario: scenario, Why: GetReason(scenario, version, hasInputSession)))
                .Where(entry => entry.Why != null)
                .ToList();

            var text = new StringBuilder();
            text.AppendLine($"# Headset checklist, {title}");
            text.AppendLine();
            var versionName = version == GameVersion.Gothic1 ? "Gothic 1" : "Gothic 2";
            text.AppendLine($"What the automated scenarios don't check in a {versionName} run" +
                $"{(hasInputSession ? " with its input session" : string.Empty)}: every @manual scenario and the " +
                "scenarios skipped for their tag. Play each one and tick it off. Marvin's console gets you there: " +
                "`goto waypoint <name>`, `set time <hour> <minute>`, `insert <instance>`; developer switches are " +
                "fields of the DeveloperConfig, Gothic.ini options are in the game's menu.");
            text.AppendLine();
            if (picked.Count == 0)
            {
                text.AppendLine("Nothing to check by hand.");
                return text.ToString();
            }

            foreach (var feature in picked.GroupBy(entry => entry.Scenario.FeatureFile).OrderBy(group => group.Key))
                AppendFeature(text, feature.ToList());

            return text.ToString();
        }

        /// <summary>
        /// Why the scenario is on the checklist, null if it isn't.
        /// </summary>
        public static string GetReason(ScenarioCase scenario, GameVersion version, bool hasInputSession)
        {
            if (scenario.HasTag("manual"))
                return "manual";
            if (scenario.HasTag("flat") && !scenario.HasTag("vr"))
                return "@flat - the Flat module doesn't run yet";

            var isG1 = scenario.HasTag("g1");
            if (isG1 != scenario.HasTag("g2") && isG1 != (version == GameVersion.Gothic1))
                return $"@{(isG1 ? "g1" : "g2")} - needs a {(isG1 ? "Gothic 1" : "Gothic 2")} run";

            if (scenario.HasTag("input") && !hasInputSession)
                return "@input - runs in the input session (menu: Run behaviour scenarios (input))";

            return null;
        }

        private static void AppendFeature(StringBuilder text,
            IReadOnlyList<(ScenarioCase Scenario, string Why)> entries)
        {
            var first = entries[0].Scenario;
            text.AppendLine($"## {first.FeatureName}");
            text.AppendLine();
            var adr = first.GetTagValue("adr");
            text.AppendLine($"`{first.FeatureFile}`{(adr != null ? $", decision record `Docs/adr/{adr}.md`" : "")}.");
            text.AppendLine();

            var background = first.Steps.Where(step => step.IsBackground).ToList();
            if (background.Count > 0)
            {
                text.AppendLine($"Before each: {DescribeSetUp(background)}");
                text.AppendLine();
            }

            foreach (var (scenario, why) in entries)
            {
                text.AppendLine($"- [ ] **{scenario.Name}** ({why}, line {scenario.Line})");
                var own = scenario.Steps.Where(step => !step.IsBackground).ToList();
                var setUp = DescribeSetUp(own, false);
                if (setUp.Length > 0)
                    text.AppendLine($"  - Set up: {setUp}");
                foreach (var step in own)
                    text.AppendLine($"  - {step.Keyword} {step.Text}");
            }

            text.AppendLine();
        }

        /// <summary>
        /// The world, way point, time, switches and options the steps name, with the Marvin command to get there.
        /// </summary>
        private static string DescribeSetUp(IEnumerable<ScenarioStep> steps, bool isAllSteps = true)
        {
            var parts = new List<string>();
            var others = new List<string>();
            foreach (var step in steps)
            {
                Match match;
                if ((match = _worldStep.Match(step.Text)).Success)
                    parts.Add($"world {match.Groups[1].Value}");
                else if ((match = _wayPointStep.Match(step.Text)).Success)
                    parts.Add($"way point {match.Groups[1].Value} (`goto waypoint {match.Groups[1].Value}`)");
                else if ((match = _timeStep.Match(step.Text)).Success)
                    parts.Add($"{match.Groups[1].Value}:{match.Groups[2].Value} " +
                        $"(`set time {match.Groups[1].Value} {match.Groups[2].Value}`)");
                else if ((match = _switchStep.Match(step.Text)).Success)
                    parts.Add($"developer switch {match.Groups[1].Value} {match.Groups[2].Value}");
                else if ((match = _iniStep.Match(step.Text)).Success)
                    parts.Add($"Gothic.ini {match.Groups[1].Value} {match.Groups[2].Value}");
                else if (isAllSteps)
                    others.Add(step.Text);
            }

            parts.AddRange(others);
            return string.Join(", ", parts);
        }
    }
}
