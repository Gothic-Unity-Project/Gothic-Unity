using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using Gothic.Core.Logging;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Report;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;
using Random = UnityEngine.Random;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// Every scenario of the feature files is one Play Mode test here. The game boots once per play session (the first
    /// "the world ... is loaded" step, GameSession) and stays loaded for the scenarios after it. Each scenario's
    /// outcome goes into the run's report (RunReport) as it ends.
    /// A scenario tagged @known-bug that fails is inconclusive for the Test Runner (the bug is known, nothing new
    /// broke); one that passes is reported as a bug that seems gone.
    /// </summary>
    public class ScenarioRunner
    {
        private const float _defaultTimeoutSeconds = 120f;
        // Kept per scenario for the report: the last warnings and errors.
        private const int _warningsKept = 20;

        private static readonly LogType[] _warningTypes =
            { LogType.Warning, LogType.Error, LogType.Exception, LogType.Assert };

        private ScenarioStep _currentStep;


        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RunReport.Begin();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            var hasInputSession = GameSession.Input != null;
            try
            {
                GameSession.End();
            }
            finally
            {
                RunReport.End(GameSession.Version, hasInputSession);
            }
        }

        [UnityTest]
        [TestCaseSource(typeof(FeatureCatalog), nameof(FeatureCatalog.Scenarios))]
        public IEnumerator Scenario(ScenarioCase scenario)
        {
            // The game logs errors of its own while it runs (missing assets, script warnings) - no test failures.
            LogAssert.ignoreFailingMessages = true;

            var result = RunReport.Start(scenario, GameSession.Version);
            var loadSecondsBefore = GameSession.TotalLoadSeconds;
            var scope = new ScenarioScope(scenario.TestName);
            _currentStep = null;

            try
            {
                // The steps' exceptions are caught around each MoveNext: no yield inside a try with a catch.
                var steps = RunSteps(scenario, scope);
                Exception failure = null;
                while (true)
                {
                    try
                    {
                        if (!steps.MoveNext())
                            break;
                    }
                    catch (Exception e)
                    {
                        failure = e;
                        break;
                    }

                    yield return steps.Current;
                }

                var status = GetStatus(scenario, failure);
                // Taken before the undo: the picture shows the state the step failed in.
                if (status is ScenarioResult.Failed or ScenarioResult.KnownBug)
                    yield return Probe.Screenshot(RunReport.AddImage(scenario.TestName, "failure", false));

                yield return scope.EndOverFrames();
                scope.End();
                var message = GetMessage(failure);
                RunReport.Finish(result, status, message, failure != null ? _currentStep?.ToString() : null,
                    GameSession.TotalLoadSeconds - loadSecondsBefore, scope.Log.Last(_warningsKept, _warningTypes),
                    scope.Log.CountOf(LogType.Warning),
                    scope.Log.CountOf(LogType.Error, LogType.Exception, LogType.Assert));

                if (status == ScenarioResult.Passed && result.IsKnownBug)
                    Logger.LogWarning($"[BehaviourTests] {scenario.TestName} passes although it is tagged " +
                        "@known-bug - the bug seems gone: remove the tag", LogCat.Debug);
                if (status == ScenarioResult.KnownBug)
                    Assert.Inconclusive($"Known bug, still there: {_currentStep}: {message}");
                if (failure != null)
                    ExceptionDispatchInfo.Capture(failure).Throw();
            }
            finally
            {
                // Also when the run stops inside the scenario (play mode ended, the framework's time limit).
                scope.End();
            }
        }

        private IEnumerator RunSteps(ScenarioCase scenario, ScenarioScope scope)
        {
            if (scenario.ParseError != null)
                Assert.Fail(scenario.ParseError);

            IgnoreIfOtherVersion(scenario);
            GameSession.Prepare(scenario.HasTag("input"));

            // All bindings first: a scenario with a missing step fails before it boots the game.
            var bindings = scenario.Steps.Select(step => (Step: step, Binding: StepRegistry.Find(step))).ToList();
            var timeoutSeconds = GetTimeoutSeconds(scenario);
            var watch = Stopwatch.StartNew();

            // The game's only random source: the same rolls each run of the scenario.
            Random.InitState(GetStableHash(scenario.TestName));
            var instances = new StepInstances(scope);
            foreach (var (step, binding) in bindings)
            {
                _currentStep = step;
                // Iterated here, not handed to Unity as a nested coroutine: its exceptions must fail this test.
                var routine = StepRoutine.Run(step,
                    () => binding.Invoke(instances.Get(binding.Method.DeclaringType), step.Text), watch,
                    timeoutSeconds, () => GameSession.IsLoading);
                while (routine.MoveNext())
                    yield return routine.Current;
            }
        }

        private static string GetStatus(ScenarioCase scenario, Exception failure)
        {
            return failure switch
            {
                null or SuccessException => ScenarioResult.Passed,
                IgnoreException => ScenarioResult.Skipped,
                InconclusiveException => ScenarioResult.Inconclusive,
                _ when scenario.HasTag("known-bug") => ScenarioResult.KnownBug,
                _ => ScenarioResult.Failed
            };
        }

        /// <summary>
        /// The failure's message without the step it names in front (the report shows the step apart).
        /// </summary>
        private string GetMessage(Exception failure)
        {
            if (failure == null)
                return null;

            var prefix = _currentStep != null ? $"{_currentStep}: " : null;
            return prefix != null && failure.Message.StartsWith(prefix, StringComparison.Ordinal)
                ? failure.Message[prefix.Length..]
                : failure.Message;
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
