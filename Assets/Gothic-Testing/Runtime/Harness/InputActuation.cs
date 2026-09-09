using UnityEngine;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// One simulated input, as it is handed to the trace. (ADR-0001 §3.5)
    ///
    /// The harness emits these itself instead of inferring them from the input system afterwards: the trace has to
    /// state what the test *did*, and a driver which reconstructed its own actions from device state would be
    /// reporting a guess.
    /// </summary>
    public readonly struct InputActuation
    {
        /// <summary>Trace event type, i.e. >input.key< or >input.mouse<.</summary>
        public readonly string Type;

        /// <summary>The control which was actuated, i.e. >W< or >rightButton< or >delta<.</summary>
        public readonly string Control;

        /// <summary>>down<, >up< or >set<.</summary>
        public readonly string State;

        /// <summary>Value of an analogue actuation, i.e. a mouse delta. Zero for buttons.</summary>
        public readonly Vector2 Value;


        public InputActuation(string type, string control, string state, Vector2 value = default)
        {
            Type = type;
            Control = control;
            State = state;
            Value = value;
        }


        public override string ToString()
        {
            return Value == Vector2.zero
                ? $"{Type} {Control} {State}"
                : $"{Type} {Control} {State} {Value}";
        }
    }
}
