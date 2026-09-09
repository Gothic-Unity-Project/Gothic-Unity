namespace Gothic.Testing.Harness
{
    /// <summary>
    /// What a scenario may decide about its own session. Everything here has a default which is correct for the
    /// smoke lane, so a fixture usually passes nothing at all.
    /// </summary>
    public class TestSessionOptions
    {
        /// <summary>
        /// Name of the DeveloperConfig this session booted with. Recorded in the manifest only - selecting the
        /// config is the fixture's job, via GOTHIC_TEST_CONFIG. (ADR-0001 D5)
        /// </summary>
        public string ConfigName;

        /// <summary>
        /// Fixed RNG seed. Null means "take it from the command line / environment, or make one up" - either way
        /// the value ends up in manifest.json and the run stays reproducible. (ADR-0001 D13)
        /// </summary>
        public int? Seed;

        /// <summary>
        /// Frames per second the session advances time by. Not a performance setting: it decouples every
        /// timestamp, timeout and screenshot in the session from how fast the machine happens to be.
        /// Zero switches capture pacing off, i.e. the game runs in real time. (ADR-0001 §3.5)
        /// </summary>
        public float CaptureFrameRate = 30f;

        /// <summary>
        /// Seconds of no bus event and no player movement before the watchdog fails the current step. A single
        /// step can raise its own budget via >Session.Step(name, watchdogSeconds:)<. (ADR-0001 §3.6)
        /// </summary>
        public float WatchdogSeconds = 30f;

        /// <summary>
        /// Stall budget for the stretch before the first step opens, i.e. the boot. Generous on purpose: a cold
        /// static cache turns a ~10s boot into ~45s and pre-caching is legitimately quiet while it runs, so the
        /// per-step budget above would fail a perfectly healthy start.
        /// </summary>
        public float BootWatchdogSeconds = 120f;

        /// <summary>
        /// Keep playing while the Editor window is in the background. Without it a local run stops the moment the
        /// developer alt-tabs away, which looks exactly like a stall.
        /// </summary>
        public bool RunInBackground = true;
    }
}
