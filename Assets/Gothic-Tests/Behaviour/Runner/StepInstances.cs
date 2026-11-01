using System;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Tests.Behaviour.Session;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// One object per step class for a scenario, so steps of a class share state within it (e.g. the item a hand holds).
    /// Injected once the game is ready - before that the container is the boot's, not the player's.
    /// </summary>
    public class StepInstances
    {
        private readonly Dictionary<Type, object> _instances = new();
        private readonly HashSet<object> _injected = new();

        public object Get(Type type)
        {
            if (!_instances.TryGetValue(type, out var instance))
                _instances[type] = instance = Activator.CreateInstance(type);

            if (GameSession.IsReady && _injected.Add(instance))
                instance.Inject();

            return instance;
        }
    }
}
