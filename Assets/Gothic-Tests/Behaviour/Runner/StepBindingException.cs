using System;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// A step has no binding or more than one.
    /// </summary>
    public class StepBindingException : Exception
    {
        public StepBindingException(string message) : base(message)
        {
        }
    }
}
