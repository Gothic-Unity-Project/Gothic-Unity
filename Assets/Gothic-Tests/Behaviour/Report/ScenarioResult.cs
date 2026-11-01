using System;

namespace Gothic.Tests.Behaviour.Report
{
    /// <summary>
    /// One scenario's outcome, a line of a run's results.jsonl (JsonUtility, the field names are the keys). Written as
    /// soon as the scenario ends, so a crash loses at most the scenario that was running. The report is built from
    /// these lines only - the dev site and the editor menu read the same format.
    /// </summary>
    [Serializable]
    public class ScenarioResult
    {
        public const string Passed = "passed";
        public const string Failed = "failed";
        public const string KnownBug = "known-bug";
        public const string Inconclusive = "inconclusive";
        public const string Skipped = "skipped";
        public const string Aborted = "aborted";

        /// <summary>
        /// "&lt;feature file&gt; / &lt;scenario&gt;", the Test Runner's name.
        /// </summary>
        public string Test;
        public string FeatureFile;
        public string Feature;
        public string Scenario;
        public int Line;
        public string[] Tags;
        public string World;
        public string GameVersion;
        public string StartedAt;

        /// <summary>
        /// passed, failed, known-bug (failed as the tag says), inconclusive (no verdict), skipped, aborted (the run
        /// stopped inside it).
        /// </summary>
        public string Status;

        /// <summary>
        /// Tagged @known-bug: a failure is expected, a pass means the bug is gone.
        /// </summary>
        public bool IsKnownBug;

        public string FailingStep;
        public string Message;
        public float Seconds;

        /// <summary>
        /// Seconds of it the game spent booting or loading a world.
        /// </summary>
        public float LoadSeconds;

        public long MemoryBefore;
        public long MemoryAfter;

        /// <summary>
        /// Screenshots and probe images, relative to the run folder.
        /// </summary>
        public string[] Images;

        public int WarningCount;
        public int ErrorCount;

        /// <summary>
        /// The scenario's last warnings and errors.
        /// </summary>
        public string[] LastWarnings;

        public float MemoryDeltaMb => (MemoryAfter - MemoryBefore) / (1024f * 1024f);
    }
}
