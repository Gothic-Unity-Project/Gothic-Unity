using System;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Testing.Recording;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;
using Random = UnityEngine.Random;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// One booted game, driven from beginning to end. (ADR-0001 D4, D6)
    ///
    /// A session - not a single test - is the unit of isolation, because booting Gothic costs ~10 seconds with a
    /// warm static cache and ~45 without. The session owns everything which has to be shared by the things
    /// looking at that boot: the run directory the artifacts land in, the one clock they are all timestamped
    /// against, the watchdog, and the input and driver surfaces the scenario acts through.
    ///
    /// It also owns the two global settings a deterministic run needs, and restores both on dispose:
    /// - >Random.InitState(seed)<, with the seed written to manifest.json (ADR-0001 D13, §3.9)
    /// - >Time.captureDeltaTime<, so pacing does not depend on the machine (ADR-0001 §3.5)
    ///
    /// Recorders attach to it rather than being created by it: <see cref="StepBegan"/>, <see cref="StepEnded"/>,
    /// <see cref="Events"/> and <see cref="InputDriver.Actuated"/> are the seams the log, screenshot and trace
    /// recorders hang off.
    /// </summary>
    public class TestSession : IDisposable
    {
        private readonly List<TestStep> _steps = new();

        private HarnessRunner _runner;
        private SessionManifestEntry _manifestEntry;

        private float _previousCaptureDeltaTime;
        private bool _previousRunInBackground;
        private bool _isDisposed;


        private TestSession(string scenarioName, TestSessionOptions options)
        {
            ScenarioName = scenarioName;
            Options = options;

            Directory = TestRunDirectory.ForScenario(scenarioName);
            Clock = new SessionClock();

            Events = new GlobalEventObserver();
            Player = new PlayerLocator();
            Watchdog = new Watchdog(Clock, Player, Events, options.WatchdogSeconds, options.BootWatchdogSeconds);

            Input = new InputDriver();
            Driver = new GameDriver(this, Input, Player);
        }


        /// <summary>
        /// The running session, or null. Recorders and diagnostics reach for it; scenarios hold their own
        /// reference and never need it.
        /// </summary>
        public static TestSession Current { get; private set; }

        public string ScenarioName { get; }
        public TestSessionOptions Options { get; }

        /// <summary>
        /// >[run root]/[scenario]/< - where this session's logs, screenshots and trace are written.
        /// </summary>
        public string Directory { get; }

        public int Seed { get; private set; }

        public SessionClock Clock { get; }
        public GlobalEventObserver Events { get; }
        public PlayerLocator Player { get; }
        public Watchdog Watchdog { get; }
        public InputDriver Input { get; }
        public GameDriver Driver { get; }

        public TestStep CurrentStep { get; private set; }

        public IReadOnlyList<TestStep> Steps => _steps;

        public event Action<TestStep> StepBegan;
        public event Action<TestStep> StepEnded;


        /// <summary>
        /// Boot a session. Called once per fixture, before the game itself is started, because the seed has to be
        /// set before anything draws from the RNG and the recorders have to be listening before the first event.
        /// </summary>
        public static TestSession Start(string scenarioName, TestSessionOptions options = null)
        {
            if (Current != null)
            {
                throw new InvalidOperationException(
                    $"A session for >{Current.ScenarioName}< is still running. Sessions take over global state " +
                    "(input devices, RNG, capture pacing) and cannot overlap.");
            }

            var session = new TestSession(scenarioName, options ?? new TestSessionOptions());
            Current = session;

            session.Begin();

            return session;
        }

        /// <summary>
        /// Open a named section of the playthrough. Dispose it to close it - in practice via >using<:
        ///
        /// <code>
        /// using (Session.Step("walk-to-arena"))
        /// {
        ///     yield return Driver.WalkForward(seconds: 6f);
        /// }
        /// </code>
        ///
        /// >watchdogSeconds< raises the stall budget for this step alone; a cold world load legitimately spends a
        /// long time without moving the player. Zero means "use the session default".
        /// </summary>
        public TestStep Step(string name, float watchdogSeconds = 0f)
        {
            if (CurrentStep is { IsFinished: false })
            {
                // Steps do not nest. Closing the open one keeps the trace readable instead of failing the
                // scenario over a bookkeeping mistake - but it is a mistake, so it is logged.
                Logger.LogWarning($"Step >{CurrentStep.Name}< was still open when >{name}< began. Closing it.",
                    LogCat.Test);
                CurrentStep.Dispose();
            }

            var step = new TestStep(this, name, _steps.Count + 1, watchdogSeconds);
            _steps.Add(step);
            CurrentStep = step;

            Watchdog.BeginStep(step);

            Logger.Log($"Step {step.Index} >{step.Name}< began at {step.StartSeconds:F2}s.", LogCat.Test);
            StepBegan?.Invoke(step);

            return step;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;

            if (CurrentStep is { IsFinished: false })
                CurrentStep.Dispose();

            RunManifest.EndSession(_manifestEntry, Clock.ElapsedSeconds, Clock.ElapsedRealtimeSeconds, _steps.Count);

            Input.Dispose();
            Events.Dispose();

            if (_runner != null)
                _runner.Shutdown();
            _runner = null;

            Time.captureDeltaTime = _previousCaptureDeltaTime;
            Application.runInBackground = _previousRunInBackground;

            Logger.Log($"Session >{ScenarioName}< ended after {Clock.ElapsedSeconds:F1}s " +
                       $"({Clock.ElapsedRealtimeSeconds:F1}s wall clock, {_steps.Count} steps). " +
                       $"Artifacts: >{Directory}<.", LogCat.Test);

            StepBegan = null;
            StepEnded = null;

            if (Current == this)
                Current = null;
        }

        internal void EndStep(TestStep step)
        {
            Logger.Log($"Step {step.Index} >{step.Name}< ended after {step.DurationSeconds:F2}s.", LogCat.Test);

            // A step which threw leaves whatever it was holding pressed. Releasing here means the next step does
            // not start with a key stuck down and a mystery to debug.
            Input.ReleaseAll();

            StepEnded?.Invoke(step);

            if (CurrentStep == step)
                CurrentStep = null;
        }

        private void Begin()
        {
            Seed = TestSeed.Resolve(Options.Seed);
            Random.InitState(Seed);

            _previousCaptureDeltaTime = Time.captureDeltaTime;
            _previousRunInBackground = Application.runInBackground;

            if (Options.CaptureFrameRate > 0f)
                Time.captureDeltaTime = 1f / Options.CaptureFrameRate;

            Application.runInBackground = Options.RunInBackground;

            Input.Setup();

            _runner = HarnessRunner.Create(this);
            _manifestEntry = RunManifest.BeginSession(ScenarioName, Directory, Options.ConfigName, Seed,
                Options.CaptureFrameRate);

            Logger.Log($"Session >{ScenarioName}< started. Seed {Seed}, " +
                       $"capture {Options.CaptureFrameRate:F0} fps, artifacts >{Directory}<.", LogCat.Test);
        }
    }
}
