using System;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Tests.Behaviour.Driver;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Tests.Behaviour.Session
{
    /// <summary>
    /// What one scenario changed, undone when it ends - pass or fail - in reverse order. Every step or driver that
    /// changes something registers how to put it back (Undo); there is no central reset. It also holds the scenario's
    /// drivers (one each, shared by its step classes) and its log capture.
    /// </summary>
    public class ScenarioScope
    {
        public readonly string ScenarioName;
        public readonly LogCapture Log;

        private readonly List<(string Description, Action Undo)> _undos = new();
        private readonly Dictionary<Type, object> _drivers = new();


        public ScenarioScope(string scenarioName)
        {
            ScenarioName = scenarioName;
            Log = new LogCapture();
            Log.Start();
        }

        /// <param name="description">What is put back, for the log if it fails ("release the right hand").</param>
        public void Undo(string description, Action undo)
        {
            _undos.Add((description, undo));
        }

        /// <summary>
        /// The scenario's driver of this type, created and injected on first use. Drivers need the running game.
        /// </summary>
        public T Driver<T>() where T : class, new()
        {
            if (_drivers.TryGetValue(typeof(T), out var driver))
                return (T)driver;

            if (!GameSession.IsReady)
                throw new InvalidOperationException($"{typeof(T).Name} used before the world is loaded - start " +
                    "the scenario (or its Background) with: the world \"...\" is loaded");

            var created = new T().Inject();
            _drivers[typeof(T)] = created;
            return created;
        }

        /// <summary>
        /// Runs the undos, the last registered first. A failing undo is logged and the others still run.
        /// </summary>
        public void End()
        {
            for (var i = _undos.Count - 1; i >= 0; i--)
            {
                var (description, undo) = _undos[i];
                try
                {
                    undo();
                }
                catch (Exception e)
                {
                    Logger.LogError($"[BehaviourTests] {ScenarioName}: undo \"{description}\" failed: {e}",
                        LogCat.Debug);
                }
            }

            _undos.Clear();
            _drivers.Clear();
            Log.Stop();
        }
    }
}
