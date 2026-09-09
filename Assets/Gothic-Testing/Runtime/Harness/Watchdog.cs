using System;
using UnityEngine;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// Detects a stalled *load*. (ADR-0001 §3.6)
    ///
    /// The failure it exists for is the frame-skipped async loader going quiet: not an exception, just "nothing
    /// happens forever". It fails the step from inside the process, while the harness still owns the artifacts.
    ///
    /// Progress is either signal: any <see cref="Gothic.Core.GlobalEventDispatcher"/> event fired, or the player
    /// moved.
    ///
    /// **Read this before expecting it to catch a gameplay problem.** >GameTimeService.TimeTick()< invokes
    /// >GameTimeSecondChangeCallback< roughly every 0.07s of real time; it is started by >WorldSceneLoaded< and
    /// stopped by >LoadingSceneLoaded<. So the moment a world is up, the bus carries a ~14Hz heartbeat, the idle
    /// timer is kicked continuously and this watchdog *cannot trip at all*. Its live window is boot, pre-caching
    /// and the load phase - where game time is stopped and bus traffic is bursty (a >CreateNpc< per NPC, scene
    /// lifecycle events), so a gap in it actually means something.
    ///
    /// That narrowness is deliberate, and the heartbeat is doing useful work by suppressing false trips: a long
    /// dialog produces no bus events whatsoever (>AI_Output< is Daedalus, the deferred observation tier), so a
    /// watchdog which ignored the >GameTime*< events would fail perfectly healthy scenarios - exactly the
    /// flakiness D11 exists to prevent. Do not "fix" it that way.
    ///
    /// What covers the other failures:
    /// - a predicate which never comes true -> the timeout on the <see cref="GameDriver.WaitUntil"/> waiting for it
    /// - the player falling through the world, a NaN transform, a frame time cliff -> Guards (§3.11), not this
    /// - a process which stops rendering entirely -> the CLI >--timeout<, since a watchdog needs frames to tick
    ///
    /// Its own contribution is narrow but real: it fails a dead load in seconds rather than at the wait's full
    /// timeout, and it timestamps *when* the game went quiet. That last part matters in this project
    /// specifically, because a Gothic port logs errors constantly (which is why the §3.3 baseline exists), so
    /// "an error was logged" does not tell you which line meant "and then it stopped".
    ///
    /// A step which is legitimately quiet for a long time raises its own budget via
    /// >Session.Step(name, watchdogSeconds:)<; the boot, which happens before any step opens, has a budget of its
    /// own (<see cref="TestSessionOptions.BootWatchdogSeconds"/>).
    /// </summary>
    public class Watchdog
    {
        /// <summary>
        /// Below this, floating point noise in a physics-driven rig reads as movement and nothing ever stalls.
        /// </summary>
        private const float _movementEpsilon = 0.01f;

        private readonly SessionClock _clock;
        private readonly PlayerLocator _player;

        private float _timeoutSeconds;
        private double _lastProgressSeconds;
        private Vector3 _lastPlayerPosition;
        private string _lastProgressReason;


        public Watchdog(SessionClock clock, PlayerLocator player, GlobalEventObserver events, float timeoutSeconds,
            float bootTimeoutSeconds)
        {
            _clock = clock;
            _player = player;

            // Until the first step opens, the session is booting - a phase with its own, much larger budget.
            _timeoutSeconds = bootTimeoutSeconds;

            DefaultTimeoutSeconds = timeoutSeconds;
            _lastPlayerPosition = player.Position;

            events.EventRaised += OnGlobalEvent;
        }


        public float DefaultTimeoutSeconds { get; }

        public bool IsEnabled { get; set; } = true;

        public bool HasTripped { get; private set; }

        /// <summary>
        /// Human readable report of what the watchdog saw last before it gave up. Written into the trace as the
        /// >watchdog.stall< event and repeated in the exception which fails the step.
        /// </summary>
        public string TripReason { get; private set; }

        /// <summary>
        /// Raised once, in the frame the watchdog trips. The recorders hang a screenshot and a trace flush off
        /// this, so the evidence is captured before the exception unwinds the scenario.
        /// </summary>
        public event Action<string> Tripped;


        /// <summary>
        /// Reset the idle timer. Called by the harness whenever it sees progress, and callable by a scenario which
        /// knows it is about to do something legitimately quiet.
        /// </summary>
        public void Kick(string reason)
        {
            _lastProgressSeconds = _clock.ElapsedSeconds;
            _lastProgressReason = reason;
        }

        /// <summary>
        /// Start watching a step with its own budget. A step which trips inherits nothing from the previous one.
        /// </summary>
        public void BeginStep(TestStep step)
        {
            _timeoutSeconds = step.WatchdogSeconds > 0f ? step.WatchdogSeconds : DefaultTimeoutSeconds;

            HasTripped = false;
            TripReason = null;

            Kick($"step.begin:{step.Name}");
        }

        /// <summary>
        /// Called once per frame by <see cref="HarnessRunner"/>.
        /// </summary>
        public void Tick()
        {
            if (!IsEnabled || HasTripped)
                return;

            CheckPlayerMovement();

            var idleSeconds = _clock.ElapsedSeconds - _lastProgressSeconds;
            if (idleSeconds < _timeoutSeconds)
                return;

            HasTripped = true;
            TripReason = $"No bus event and no player movement for {idleSeconds:F1}s " +
                         $"(budget {_timeoutSeconds:F0}s). Last progress: >{_lastProgressReason ?? "none"}<.";

            Tripped?.Invoke(TripReason);
        }

        /// <summary>
        /// Checked from inside every waiting verb, which is the only place the harness hands control back to the
        /// game - and therefore the only place a stall can be turned into a clean step failure.
        /// </summary>
        public void ThrowIfTripped()
        {
            if (HasTripped)
                throw new HarnessStallException(TripReason);
        }

        private void CheckPlayerMovement()
        {
            if (!_player.IsAvailable)
                return;

            var position = _player.Position;

            if ((position - _lastPlayerPosition).sqrMagnitude < _movementEpsilon * _movementEpsilon)
                return;

            _lastPlayerPosition = position;
            Kick("player.moved");
        }

        private void OnGlobalEvent(string eventName, object[] arguments)
        {
            Kick($"event:{eventName}");
        }
    }
}
