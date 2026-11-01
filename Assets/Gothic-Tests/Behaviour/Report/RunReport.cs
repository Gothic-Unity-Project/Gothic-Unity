using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Gothic.Core.Logging;
using Gothic.Tests.Behaviour.Runner;
using UnityEngine;
using UnityEngine.Profiling;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Tests.Behaviour.Report
{
    /// <summary>
    /// The report of one run (one play session): Logs/BehaviourTests/&lt;yyyy-mm-dd-HHmm&gt;/ with results.jsonl (a
    /// line per scenario, written as it ends), the screenshots (shots/) and probe images (probes/), and at the end
    /// report.md and checklist.md. Logs/ is git-ignored.
    /// </summary>
    public static class RunReport
    {
        public const string ResultsFileName = "results.jsonl";
        public const string ReportFileName = "report.md";
        public const string ChecklistFileName = "checklist.md";

        private const string _shotsFolderName = "shots";
        private const string _probesFolderName = "probes";

        public static string Root => Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Logs",
            "BehaviourTests");

        /// <summary>
        /// The run's folder, null before Begin.
        /// </summary>
        public static string Folder { get; private set; }

        private static ScenarioResult _open;
        private static float _openSince;
        private static List<string> _openImages;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Folder = null;
            _open = null;
            _openImages = null;
        }

        /// <summary>
        /// A new run folder, named by the minute it starts (a second run in the same minute gets "-2").
        /// </summary>
        public static void Begin()
        {
            var name = DateTime.Now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture);
            var folder = Path.Combine(Root, name);
            for (var i = 2; Directory.Exists(folder); i++)
                folder = Path.Combine(Root, $"{name}-{i}");

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, ResultsFileName), string.Empty);
            Folder = folder;
            _open = null;
            Logger.Log($"[BehaviourTests] Report of this run: {folder}", LogCat.Debug);
        }

        /// <summary>
        /// The scenario starts: its time and memory are taken from here.
        /// </summary>
        public static ScenarioResult Start(ScenarioCase scenario, GameVersion version)
        {
            if (Folder == null)
                Begin();
            if (_open != null)
                Finish(_open, ScenarioResult.Aborted, "The next scenario started before this one ended", null);

            _open = new ScenarioResult
            {
                Test = scenario.TestName,
                FeatureFile = scenario.FeatureFile,
                Feature = scenario.FeatureName,
                Scenario = scenario.Name,
                Line = scenario.Line,
                Tags = scenario.Tags.ToArray(),
                World = scenario.World,
                GameVersion = version.ToString(),
                StartedAt = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                IsKnownBug = scenario.HasTag("known-bug"),
                MemoryBefore = Profiler.GetTotalAllocatedMemoryLong()
            };
            _openSince = Time.realtimeSinceStartup;
            _openImages = new List<string>();
            return _open;
        }

        /// <summary>
        /// An image of the running scenario, listed with its result. Returns where to save it.
        /// </summary>
        /// <param name="isProbe">A probe's image (probes/) or a screenshot (shots/).</param>
        public static string AddImage(string scenarioName, string name, bool isProbe)
        {
            if (Folder == null)
                Begin();

            var file = ToFileName($"{scenarioName} - {name}") + ".png";
            var relative = $"{(isProbe ? _probesFolderName : _shotsFolderName)}/{file}";
            _openImages?.Add(relative);
            var path = Path.Combine(Folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        /// <summary>
        /// The scenario ended: its line goes into results.jsonl at once.
        /// </summary>
        public static void Finish(ScenarioResult result, string status, string message, string failingStep,
            float loadSeconds = 0f, IReadOnlyList<string> lastWarnings = null, int warningCount = 0,
            int errorCount = 0)
        {
            result.Status = status;
            result.Message = message;
            result.FailingStep = failingStep;
            result.Seconds = Time.realtimeSinceStartup - _openSince;
            result.LoadSeconds = loadSeconds;
            result.MemoryAfter = Profiler.GetTotalAllocatedMemoryLong();
            result.Images = _openImages?.ToArray() ?? Array.Empty<string>();
            result.LastWarnings = lastWarnings?.ToArray() ?? Array.Empty<string>();
            result.WarningCount = warningCount;
            result.ErrorCount = errorCount;

            if (Folder != null)
                File.AppendAllText(Path.Combine(Folder, ResultsFileName), JsonUtility.ToJson(result) + "\n");
            if (_open == result)
            {
                _open = null;
                _openImages = null;
            }
        }

        /// <summary>
        /// The run is over: report.md and checklist.md from the results so far.
        /// </summary>
        /// <param name="hasInputSession">The run had an input session: its @input scenarios aren't on the checklist.
        /// </param>
        public static void End(GameVersion version, bool hasInputSession)
        {
            if (Folder == null)
                return;

            if (_open != null)
                Finish(_open, ScenarioResult.Aborted, "The run ended inside the scenario (play mode stopped, the " +
                    "test framework's time limit)", null);

            try
            {
                Write(Folder, version, hasInputSession);
                Logger.Log($"[BehaviourTests] Report written: {Path.Combine(Folder, ReportFileName)}", LogCat.Debug);
            }
            catch (Exception e)
            {
                Logger.LogError($"[BehaviourTests] The report of {Folder} couldn't be written: {e}", LogCat.Debug);
            }
        }

        /// <summary>
        /// report.md and checklist.md of a run folder from its results.jsonl - also for a run that crashed.
        /// </summary>
        public static void Write(string folder, GameVersion version, bool hasInputSession)
        {
            var results = ReadResults(folder);
            var catalog = FeatureCatalog.LoadAll();
            var runName = Path.GetFileName(folder);
            File.WriteAllText(Path.Combine(folder, ReportFileName),
                ReportWriter.Build(runName, results, catalog));

            var features = results.Select(result => result.FeatureFile).ToHashSet();
            var scenarios = catalog.Where(scenario => features.Contains(scenario.FeatureFile)).ToList();
            File.WriteAllText(Path.Combine(folder, ChecklistFileName),
                ChecklistWriter.Build($"run {runName}", scenarios, version, hasInputSession));
        }

        public static List<ScenarioResult> ReadResults(string folder)
        {
            var path = Path.Combine(folder, ResultsFileName);
            if (!File.Exists(path))
                return new List<ScenarioResult>();

            return File.ReadAllLines(path).Where(line => line.Trim().Length > 0)
                .Select(JsonUtility.FromJson<ScenarioResult>).ToList();
        }

        /// <summary>
        /// The newest run folder, null if there is none.
        /// </summary>
        public static string FindLastRun()
        {
            if (!Directory.Exists(Root))
                return null;

            return Directory.GetDirectories(Root)
                .Where(folder => File.Exists(Path.Combine(folder, ResultsFileName)))
                .OrderByDescending(folder => File.GetLastWriteTimeUtc(Path.Combine(folder, ResultsFileName)))
                .FirstOrDefault();
        }

        private static string ToFileName(string text)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(text.Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c));
        }
    }
}
