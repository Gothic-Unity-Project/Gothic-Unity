using System;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// One named section of a playthrough, opened by <see cref="TestSession.Step"/> and closed by disposing it:
    ///
    /// <code>
    /// using (Session.Step("walk-to-arena"))
    /// {
    ///     yield return Driver.WalkForward(seconds: 6f);
    /// }
    /// </code>
    ///
    /// A step is what turns a wall of trace lines into something readable: it brackets the trace, names every
    /// screenshot taken inside it and, on failure, names the flushed replay clip. (ADR-0001 §3.2, §3.4)
    /// </summary>
    public class TestStep : IDisposable
    {
        private readonly TestSession _session;


        internal TestStep(TestSession session, string name, int index, float watchdogSeconds)
        {
            _session = session;

            Name = name;
            Index = index;
            WatchdogSeconds = watchdogSeconds;
            StartSeconds = session.Clock.ElapsedSeconds;
            StartFrame = session.Clock.Frame;
        }


        public string Name { get; }
        public int Index { get; }

        /// <summary>
        /// How long this step may make no progress at all before the watchdog fails it. (ADR-0001 §3.6)
        /// </summary>
        public float WatchdogSeconds { get; }

        public double StartSeconds { get; }
        public int StartFrame { get; }

        public double EndSeconds { get; private set; }
        public int EndFrame { get; private set; }

        public bool IsFinished { get; private set; }

        public double DurationSeconds => (IsFinished ? EndSeconds : _session.Clock.ElapsedSeconds) - StartSeconds;


        public void Dispose()
        {
            // A scenario may dispose a step twice (an explicit Dispose inside a using block); ending it twice
            // would emit a second step.end trace event and confuse every reader of the timeline.
            if (IsFinished)
                return;

            EndSeconds = _session.Clock.ElapsedSeconds;
            EndFrame = _session.Clock.Frame;
            IsFinished = true;

            _session.EndStep(this);
        }
    }
}
