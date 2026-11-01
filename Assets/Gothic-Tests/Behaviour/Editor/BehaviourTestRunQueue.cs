using System.Linq;
using Gothic.Core.Logging;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Tests.Behaviour.Editor
{
    /// <summary>
    /// Runs the behaviour scenarios from the menu, one play session per run: the fast run, the input session, or both
    /// one after the other. Before each run the editor reloads its scripts, so no state of an earlier play session
    /// survives into the next (an editor set to enter play mode without a domain reload keeps the game's static
    /// event listeners, and the next boot hangs on them). The queue lives in SessionState, across the reloads.
    /// </summary>
    [InitializeOnLoad]
    public static class BehaviourTestRunQueue
    {
        public const string Fast = "fast";
        public const string Input = "input";

        private const string _assemblyName = "Gothic.Tests.Behaviour";
        private const string _queueKey = "Gothic.BehaviourTests.Queue";
        private const string _isStartPendingKey = "Gothic.BehaviourTests.IsStartPending";

        // Category filters: '!' excludes, the names are regular expressions.
        private static readonly string[] _fastCategories = { "!^input$", "!^visual$", "!^slow$", "!^manual$" };
        private static readonly string[] _inputCategories = { "^input$" };


        static BehaviourTestRunQueue()
        {
            TestRunnerApi.RegisterTestCallback(new BehaviourTestRunCallbacks());
            // EditorApplication.update, not delayCall: an editor in the background pumped no delay calls after the
            // reload (seen with the run started through Unity MCP), it still ticks its update.
            if (SessionState.GetBool(_isStartPendingKey, false))
                EditorApplication.update += StartNextWhenInEditMode;
        }

        /// <summary>
        /// Queues the runs and starts the first after a script reload.
        /// </summary>
        public static void Start(params string[] runs)
        {
            SessionState.SetString(_queueKey, string.Join(";", runs));
            ReloadAndStartNext();
        }

        private static void ReloadAndStartNext()
        {
            SessionState.SetBool(_isStartPendingKey, true);
            EditorUtility.RequestScriptReload();
        }

        private static void StartNextWhenInEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;

            EditorApplication.update -= StartNextWhenInEditMode;
            SessionState.SetBool(_isStartPendingKey, false);
            var queue = SessionState.GetString(_queueKey, string.Empty).Split(';').Where(run => run.Length > 0)
                .ToList();
            if (queue.Count == 0)
                return;

            var run = queue[0];
            SessionState.SetString(_queueKey, string.Join(";", queue.Skip(1)));
            Logger.Log($"[BehaviourTests] Starting the {run} run" +
                $"{(queue.Count > 1 ? $", then: {string.Join(", ", queue.Skip(1))}" : string.Empty)}", LogCat.Debug);

            var filter = new Filter
            {
                testMode = TestMode.PlayMode,
                assemblyNames = new[] { _assemblyName },
                categoryNames = run == Input ? _inputCategories : _fastCategories
            };
            ScriptableObject.CreateInstance<TestRunnerApi>().Execute(new ExecutionSettings(filter));
        }

        /// <summary>
        /// A run ended (any run, also one not started here): the next queued one starts after a script reload.
        /// </summary>
        public static void OnRunFinished()
        {
            if (SessionState.GetString(_queueKey, string.Empty).Length == 0)
                return;

            EditorApplication.update += ReloadWhenInEditMode;
        }

        private static void ReloadWhenInEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EditorApplication.update -= ReloadWhenInEditMode;
            ReloadAndStartNext();
        }
    }
}
