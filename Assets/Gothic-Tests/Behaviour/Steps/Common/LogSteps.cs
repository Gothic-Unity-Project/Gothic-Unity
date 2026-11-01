using System.Collections;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;

namespace Gothic.Tests.Behaviour.Steps.Common
{
    /// <summary>
    /// The scenario's log (LogCapture), for what leaves no state behind: an NPC's overlay line, a reason a service
    /// logs. Where state exists, a step checks the state instead. Texts match case-insensitively, anywhere in a line.
    /// </summary>
    public class LogSteps
    {
        private readonly ScenarioScope _scope;


        public LogSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

        [Step("the log contains \"(.*)\" within (\\d+) s")]
        public IEnumerator TheLogContainsWithin(string text, int seconds)
        {
            yield return Wait.Until(() => _scope.Log.Count(text) > 0, seconds, $"a log line with \"{text}\"",
                () => $"there's none in the scenario's {_scope.Log.Received} lines");
        }

        /// <summary>
        /// Waits the whole time - something that shows up late (a second perception) still counts.
        /// </summary>
        [Step("the log contains \"(.*)\" exactly (\\d+) times? within (\\d+) s")]
        public IEnumerator TheLogContainsExactlyWithin(string text, int count, int seconds)
        {
            yield return Wait.Seconds(seconds);
            Assert.That(_scope.Log.Count(text), Is.EqualTo(count), $"Log lines with \"{text}\"");
        }
    }
}
