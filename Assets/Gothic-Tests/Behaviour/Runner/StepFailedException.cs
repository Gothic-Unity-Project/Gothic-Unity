using System;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// A step threw something other than an assertion - a bug in the step or the game. Names the step.
    /// </summary>
    public class StepFailedException : Exception
    {
        public StepFailedException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
