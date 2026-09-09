using UnityEngine;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// The single clock every recorder timestamps against, so log lines, trace events and screenshots of one
    /// session can be merged into one timeline. (ADR-0001 D6, §3.5)
    ///
    /// It reads unscaled time on purpose. With >Time.captureDeltaTime< set (§3.5) unscaled time advances by
    /// exactly one capture step per frame, which makes every timestamp and every timeout in the harness a frame
    /// count in disguise - identical on a developer machine and on a loaded CI box. Scaled time would not do:
    /// Gothic's menus stop the game clock, and a timeout which stops ticking with it would hang forever.
    /// </summary>
    public class SessionClock
    {
        private readonly double _startSeconds;
        private readonly float _startRealtime;
        private readonly int _startFrame;


        public SessionClock()
        {
            _startSeconds = Time.unscaledTimeAsDouble;
            _startRealtime = Time.realtimeSinceStartup;
            _startFrame = Time.frameCount;
        }


        /// <summary>
        /// Seconds since session start, in captured time - the >t< field of a trace event.
        /// </summary>
        public double ElapsedSeconds => Time.unscaledTimeAsDouble - _startSeconds;

        /// <summary>
        /// Wall clock seconds since session start. Reported in the manifest so a slow runner is visible, never
        /// used for a timeout - that is what <see cref="ElapsedSeconds"/> exists for.
        /// </summary>
        public double ElapsedRealtimeSeconds => Time.realtimeSinceStartup - _startRealtime;

        /// <summary>
        /// Rendered frames since session start - the >frame< field of a trace event.
        /// </summary>
        public int Frame => Time.frameCount - _startFrame;

        /// <summary>
        /// Physics steps since session start - the >fixed< field of a trace event. Counted by
        /// <see cref="HarnessRunner"/> because Unity exposes no fixed frame counter.
        /// </summary>
        public int FixedFrame { get; private set; }


        internal void CountFixedFrame()
        {
            FixedFrame++;
        }
    }
}
