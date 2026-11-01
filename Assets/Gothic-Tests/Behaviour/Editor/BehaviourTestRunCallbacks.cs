using UnityEditor.TestTools.TestRunner.Api;

namespace Gothic.Tests.Behaviour.Editor
{
    /// <summary>
    /// Tells BehaviourTestRunQueue when a test run ends, so the next queued run can start.
    /// </summary>
    public class BehaviourTestRunCallbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun)
        {
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            BehaviourTestRunQueue.OnRunFinished();
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
        }
    }
}
