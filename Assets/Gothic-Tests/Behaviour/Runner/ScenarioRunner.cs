using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
using UnityEngine.TestTools;
using ZenKit;
using Random = UnityEngine.Random;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// Every scenario of the feature files is one Play Mode test here. The game boots once per play session (the first
    /// "the world ... is loaded" step, GameSession) and stays loaded for the scenarios after it.
    /// </summary>
    public class ScenarioRunner
    {
        private const float _defaultTimeoutSeconds = 120f;


        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            GameSession.End();
        }

        [UnityTest]
        [TestCaseSource(typeof(FeatureCatalog), nameof(FeatureCatalog.Scenarios))]
        public IEnumerator Scenario(ScenarioCase scenario)
        {
            if (scenario.ParseError != null)
                Assert.Fail(scenario.ParseError);

            // The game logs errors of its own while it runs (missing assets, script warnings) - no test failures.
            LogAssert.ignoreFailingMessages = true;
            IgnoreIfOtherVersion(scenario);

            // All bindings first: a scenario with a missing step fails before it boots the game.
            var bindings = scenario.Steps.Select(step => (Step: step, Binding: StepRegistry.Find(step))).ToList();
            var timeoutSeconds = GetTimeoutSeconds(scenario);
            var watch = Stopwatch.StartNew();

            // The game's only random source: the same rolls each run of the scenario.
            Random.InitState(GetStableHash(scenario.TestName));
            var scope = new ScenarioScope(scenario.TestName);
            var instances = new StepInstances(scope);
            try
            {
                foreach (var (step, binding) in bindings)
                {
                    // Iterated here, not handed to Unity as a nested coroutine: its exceptions must fail this test.
                    var routine = StepRoutine.Run(step,
                        () => binding.Invoke(instances.Get(binding.Method.DeclaringType), step.Text), watch,
                        timeoutSeconds, () => GameSession.IsLoading);
                    while (routine.MoveNext())
                        yield return routine.Current;
                }
            }
            finally
            {
                scope.End();
            }
        }

        /// <summary>
        /// A scenario of the other game version (@g1/@g2) is skipped - a run has one version (GameSession.Version).
        /// </summary>
        private static void IgnoreIfOtherVersion(ScenarioCase scenario)
        {
            var isG1 = scenario.HasTag("g1");
            if (isG1 == scenario.HasTag("g2"))
                return;

            if (isG1 != (GameSession.Version == GameVersion.Gothic1))
                Assert.Ignore($"@{(isG1 ? "g1" : "g2")} - this run is {GameSession.Version}");
        }

        /// <summary>
        /// string.GetHashCode may differ between runtimes; this one doesn't (FNV-1a).
        /// </summary>
        private static int GetStableHash(string text)
        {
            unchecked
            {
                var hash = (int)2166136261;
                foreach (var c in text)
                    hash = (hash ^ c) * 16777619;
                return hash;
            }
        }

        private static float GetTimeoutSeconds(ScenarioCase scenario)
        {
            var value = scenario.GetTagValue("timeout");
            return value != null ? float.Parse(value, CultureInfo.InvariantCulture) : _defaultTimeoutSeconds;
        }
    }
}
