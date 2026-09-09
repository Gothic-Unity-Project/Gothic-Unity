using System;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// Thrown when the <see cref="Watchdog"/> decided the game stopped making progress. (ADR-0001 §3.6)
    ///
    /// It exists so a stall fails the step from inside the process - with logs, screenshots and the trace already
    /// flushed - instead of letting the CLI >--timeout< kill the Editor and take the artifacts with it.
    /// </summary>
    public class HarnessStallException : Exception
    {
        public HarnessStallException(string message) : base(message)
        {
        }
    }
}
