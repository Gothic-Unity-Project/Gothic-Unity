using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// A [Step] method and its pattern.
    /// </summary>
    public class StepBinding
    {
        public readonly string Pattern;
        public readonly MethodInfo Method;
        public readonly bool IsReserved;

        private readonly Regex _regex;

        public StepBinding(StepAttribute attribute, MethodInfo method)
        {
            Pattern = attribute.Pattern;
            Method = method;
            IsReserved = attribute.IsReserved;
            _regex = new Regex($"^(?:{attribute.Pattern})$", RegexOptions.CultureInvariant);
        }

        public bool Matches(string text)
        {
            return _regex.IsMatch(text);
        }

        /// <summary>
        /// Calls the method on the step class's instance with the text's groups. Returns the step's IEnumerator or
        /// null for a void step.
        /// </summary>
        public IEnumerator Invoke(object instance, string text)
        {
            var groups = _regex.Match(text).Groups;
            var parameters = Method.GetParameters();
            if (parameters.Length != groups.Count - 1)
                throw new InvalidOperationException(
                    $"{Describe()} has {parameters.Length} parameters but the pattern {groups.Count - 1} groups");

            var arguments = parameters.Select((parameter, i) => Convert(groups[i + 1].Value, parameter.ParameterType))
                .ToArray();
            try
            {
                return Method.Invoke(instance, arguments) as IEnumerator;
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        public string Describe()
        {
            return $"{Method.DeclaringType?.Name}.{Method.Name} [\"{Pattern}\"]";
        }

        private static object Convert(string value, Type type)
        {
            if (type == typeof(string))
                return value;
            if (type == typeof(int))
                return int.Parse(value, CultureInfo.InvariantCulture);
            if (type == typeof(float))
                return float.Parse(value, CultureInfo.InvariantCulture);
            // (on|off), (is|is not), (can|cannot): the first word is true.
            if (type == typeof(bool))
                return value is "on" or "true" or "is" or "can" or "yes";
            if (type.IsEnum)
                return Enum.Parse(type, value.Replace(" ", string.Empty), true);

            throw new NotSupportedException($"Step parameters of type {type.Name} aren't supported");
        }
    }
}
