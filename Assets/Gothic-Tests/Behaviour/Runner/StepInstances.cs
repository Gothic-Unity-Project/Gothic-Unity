using System;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Tests.Behaviour.Session;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// One object per step class for a scenario, so steps of a class share state within it. A step class whose
    /// constructor takes the ScenarioScope gets the scenario's (undo, drivers, log). Injected once the game is ready -
    /// before that the container is the boot's, not the player's.
    /// </summary>
    public class StepInstances
    {
        private readonly ScenarioScope _scope;
        private readonly Dictionary<Type, object> _instances = new();
        private readonly HashSet<object> _injected = new();

        public StepInstances(ScenarioScope scope)
        {
            _scope = scope;
        }

        public object Get(Type type)
        {
            if (!_instances.TryGetValue(type, out var instance))
                _instances[type] = instance = Create(type);

            if (GameSession.IsReady && _injected.Add(instance))
                instance.Inject();

            return instance;
        }

        private object Create(Type type)
        {
            return type.GetConstructor(new[] { typeof(ScenarioScope) }) != null
                ? Activator.CreateInstance(type, _scope)
                : Activator.CreateInstance(type);
        }
    }
}
