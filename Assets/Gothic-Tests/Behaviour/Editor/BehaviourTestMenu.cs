using System;
using System.Globalization;
using System.IO;
using Gothic.Core.Logging;
using Gothic.Tests.Behaviour.Report;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using UnityEditor;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Tests.Behaviour.Editor
{
    /// <summary>
    /// Gothic > Tests: the behaviour scenario runs (fast, input, all), the manual checklist and the step catalog
    /// without starting the game, and the last run's report. Runs go through BehaviourTestRunQueue.
    /// </summary>
    public static class BehaviourTestMenu
    {
        private const string _menu = "Gothic/Tests/";
        private const int _priority = 400;

        [MenuItem(_menu + "Run behaviour scenarios (fast)", priority = _priority)]
        public static void RunFast()
        {
            BehaviourTestRunQueue.Start(BehaviourTestRunQueue.Fast);
        }

        [MenuItem(_menu + "Run behaviour scenarios (input)", priority = _priority + 1)]
        public static void RunInput()
        {
            BehaviourTestRunQueue.Start(BehaviourTestRunQueue.Input);
        }

        /// <summary>
        /// Fast, then the input session in a second play session.
        /// </summary>
        [MenuItem(_menu + "Run behaviour scenarios (all)", priority = _priority + 2)]
        public static void RunAll()
        {
            BehaviourTestRunQueue.Start(BehaviourTestRunQueue.Fast, BehaviourTestRunQueue.Input);
        }

        [MenuItem(_menu + "Run behaviour scenarios (fast)", true)]
        [MenuItem(_menu + "Run behaviour scenarios (input)", true)]
        [MenuItem(_menu + "Run behaviour scenarios (all)", true)]
        public static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>
        /// The checklist a fast run of Production's game version leaves: every @manual scenario, the @input ones and
        /// the other version's. No game start.
        /// </summary>
        [MenuItem(_menu + "Write manual checklist", priority = _priority + 20)]
        public static void WriteManualChecklist()
        {
            var path = Path.Combine(RunReport.Root, RunReport.ChecklistFileName);
            Directory.CreateDirectory(RunReport.Root);
            var title = $"written {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";
            File.WriteAllText(path, ChecklistWriter.Build(title, FeatureCatalog.LoadAll(),
                BehaviourTestConfig.GameVersion, false));
            Logger.Log($"[BehaviourTests] Checklist written: {path}", LogCat.Debug);
            EditorUtility.OpenWithDefaultApp(path);
        }

        [MenuItem(_menu + "Write step catalog", priority = _priority + 21)]
        public static void WriteStepCatalog()
        {
            StepCatalogWriter.WriteIntoDoc();
            Logger.Log($"[BehaviourTests] Step catalog written into {StepCatalogWriter.DocPath}", LogCat.Debug);
        }

        /// <summary>
        /// A run that crashed has no report.md yet - it is written from its results.jsonl first.
        /// </summary>
        [MenuItem(_menu + "Open last report", priority = _priority + 40)]
        public static void OpenLastReport()
        {
            var folder = RunReport.FindLastRun();
            if (folder == null)
            {
                Logger.LogWarning($"[BehaviourTests] No run in {RunReport.Root} yet", LogCat.Debug);
                return;
            }

            var report = Path.Combine(folder, RunReport.ReportFileName);
            if (!File.Exists(report))
                RunReport.Write(folder, BehaviourTestConfig.GameVersion, false);
            EditorUtility.OpenWithDefaultApp(report);
        }
    }
}
