using System;

namespace Gothic.Testing.Recording
{
    /// <summary>
    /// One scenario session inside a run. <see cref="Seed"/> is the field this whole file exists for: it is what
    /// turns "it failed last night" into "it fails here, now". (ADR-0001 D13, §3.9)
    /// </summary>
    [Serializable]
    public class SessionManifestEntry
    {
        public string Scenario;
        public string Directory;
        public string ConfigName;

        public int Seed;
        public float CaptureFrameRate;

        public string StartedUtc;
        public string EndedUtc;

        /// <summary>
        /// Session length in captured seconds - comparable between machines, unlike the wall clock below.
        /// </summary>
        public double DurationSeconds;

        public double DurationRealtimeSeconds;

        public int StepCount;
    }
}
