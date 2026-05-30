using System.Diagnostics;
using GUZ.Core.Logging;

namespace GUZ.Core.Extensions
{
    public static class StopwatchExtension
    {

        /// <summary>
        /// Provide a formatted output of Stopwatch entries to easily find them inside Unity's Console log.
        /// </summary>
        public static void Log(this Stopwatch stopwatch, string message)
        {
            Logger.Log($"[StopWatch] {message} [{stopwatch.Elapsed.TotalMilliseconds / 1000}s]", LogCat.Misc);
        }

        /// <summary>
        /// Strip down a log + restart to one line in your code.
        /// (Less boilerplate due to Stopwatch usage.)
        /// </summary>
        public static void LogAndRestart(this Stopwatch stopwatch, string message)
        {
            stopwatch.Log(message);
            stopwatch.Restart();
        }
    }
}
