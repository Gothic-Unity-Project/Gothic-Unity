using System;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// Thrown when a tolerant wait ran out of time. (ADR-0001 D11)
    ///
    /// The message carries the >because< the scenario passed in, because that sentence is the only thing which
    /// explains what was supposed to happen - a stack trace pointing at WaitUntil never does.
    /// </summary>
    public class WaitTimeoutException : Exception
    {
        public WaitTimeoutException(string because, float timeoutSeconds, string stepName)
            : base(BuildMessage(because, timeoutSeconds, stepName))
        {
            Because = because;
            TimeoutSeconds = timeoutSeconds;
            StepName = stepName;
        }


        public string Because { get; }
        public float TimeoutSeconds { get; }
        public string StepName { get; }


        private static string BuildMessage(string because, float timeoutSeconds, string stepName)
        {
            var step = stepName == null ? string.Empty : $" in step >{stepName}<";

            return $"Timed out after {timeoutSeconds}s{step}: {because}";
        }
    }
}
