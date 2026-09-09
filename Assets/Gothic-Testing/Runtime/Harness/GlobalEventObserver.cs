using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Gothic.Core;
using UnityEngine.Events;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// Binds a listener to every event on <see cref="GlobalEventDispatcher"/> by reflection and republishes them
    /// as one stream of >name plus arguments<. (ADR-0001 D7)
    ///
    /// The bus is already the project's single seam for "everything that happens at runtime", so reflecting over
    /// it - instead of subscribing to a hand-maintained list - means the harness stays complete as events are
    /// added. Nobody has to remember to register a new event here.
    ///
    /// Two consumers share this one binding: the <see cref="Watchdog"/> treats every event as a sign of life, and
    /// the trace recorder writes them out as the >event< lines of the timeline (ADR-0001 §3.5). Binding twice
    /// would double every trace line, so recorders subscribe to this observer rather than reflecting again.
    ///
    /// Lane B note: the generic binding below closes a generic method over runtime types. That is free in the
    /// Editor (Lane A) but needs the involved UnityEvent instantiations to survive IL2CPP stripping in a player
    /// build - to be verified when Lane B is built, not before.
    /// </summary>
    public class GlobalEventObserver : IDisposable
    {
        /// <summary>
        /// UnityEvent exists in a non-generic and in four generic flavours; anything beyond that cannot occur.
        /// </summary>
        private const int _maxEventArguments = 4;

        private readonly List<Action> _unbinds = new();

        private bool _isDisposed;


        /// <summary>
        /// Raised for every bus event: the field name as it is written in GlobalEventDispatcher, plus the
        /// arguments it carried. Arguments are handed on boxed and untouched - turning an NpcContainer into
        /// something a trace line can hold is the recorder's job, not this class's.
        /// </summary>
        public event Action<string, object[]> EventRaised;

        /// <summary>
        /// How many bus events this observer has seen. The cheap "did anything happen at all" signal.
        /// </summary>
        public int EventCount { get; private set; }

        /// <summary>
        /// Name of the most recent event, for the watchdog's stall report and for debugging a silent session.
        /// </summary>
        public string LastEventName { get; private set; }


        public GlobalEventObserver()
        {
            var fields = typeof(GlobalEventDispatcher)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => typeof(UnityEventBase).IsAssignableFrom(field.FieldType));

            foreach (var field in fields)
            {
                if (field.GetValue(null) is UnityEventBase unityEvent)
                    Bind(field.Name, field.FieldType, unityEvent);
            }
        }


        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;

            // GlobalEventDispatcher is static and outlives a session. Without this every session would leave its
            // listeners behind and the second run inside one Editor session would report every event twice.
            foreach (var unbind in _unbinds)
                unbind();

            _unbinds.Clear();
            EventRaised = null;
        }

        private void Bind(string name, Type fieldType, UnityEventBase unityEvent)
        {
            if (unityEvent is UnityEvent plainEvent)
            {
                void Handler() => Raise(name, Array.Empty<object>());

                plainEvent.AddListener(Handler);
                _unbinds.Add(() => plainEvent.RemoveListener(Handler));

                return;
            }

            if (!fieldType.IsGenericType)
                return;

            var arguments = fieldType.GetGenericArguments();
            if (arguments.Length > _maxEventArguments)
                return;

            // One generic BindArguments<...> overload per arity; reflection picks the matching one and closes it
            // over the event's own argument types, which is the only way to reach AddListener(UnityAction<T...>).
            // Selected by arity rather than by parameter types: all four overloads take (string, UnityEventBase),
            // so a type based lookup is ambiguous by construction.
            var binder = GetType()
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(method => method.Name == nameof(BindArguments) &&
                                          method.GetGenericArguments().Length == arguments.Length);

            if (binder == null)
                return;

            binder.MakeGenericMethod(arguments).Invoke(this, new object[] { name, unityEvent });
        }

        private void BindArguments<T0>(string name, UnityEventBase unityEvent)
        {
            var typedEvent = (UnityEvent<T0>)unityEvent;
            void Handler(T0 arg0) => Raise(name, new object[] { arg0 });

            typedEvent.AddListener(Handler);
            _unbinds.Add(() => typedEvent.RemoveListener(Handler));
        }

        private void BindArguments<T0, T1>(string name, UnityEventBase unityEvent)
        {
            var typedEvent = (UnityEvent<T0, T1>)unityEvent;
            void Handler(T0 arg0, T1 arg1) => Raise(name, new object[] { arg0, arg1 });

            typedEvent.AddListener(Handler);
            _unbinds.Add(() => typedEvent.RemoveListener(Handler));
        }

        private void BindArguments<T0, T1, T2>(string name, UnityEventBase unityEvent)
        {
            var typedEvent = (UnityEvent<T0, T1, T2>)unityEvent;
            void Handler(T0 arg0, T1 arg1, T2 arg2) => Raise(name, new object[] { arg0, arg1, arg2 });

            typedEvent.AddListener(Handler);
            _unbinds.Add(() => typedEvent.RemoveListener(Handler));
        }

        private void BindArguments<T0, T1, T2, T3>(string name, UnityEventBase unityEvent)
        {
            var typedEvent = (UnityEvent<T0, T1, T2, T3>)unityEvent;
            void Handler(T0 arg0, T1 arg1, T2 arg2, T3 arg3) => Raise(name, new object[] { arg0, arg1, arg2, arg3 });

            typedEvent.AddListener(Handler);
            _unbinds.Add(() => typedEvent.RemoveListener(Handler));
        }

        private void Raise(string name, object[] arguments)
        {
            if (_isDisposed)
                return;

            EventCount++;
            LastEventName = name;

            EventRaised?.Invoke(name, arguments);
        }
    }
}
