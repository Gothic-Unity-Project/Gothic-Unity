using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// All [Step] methods of this assembly (Steps/). A step's text must match exactly one of them.
    /// </summary>
    public static class StepRegistry
    {
        private static List<StepBinding> _bindings;

        public static IReadOnlyList<StepBinding> Bindings => _bindings ??= FindBindings();

        public static List<StepBinding> FindMatches(string text)
        {
            return Bindings.Where(binding => binding.Matches(text)).ToList();
        }

        /// <summary>
        /// The one binding of the step, or an exception naming what is missing or which bindings compete.
        /// </summary>
        public static StepBinding Find(ScenarioStep step)
        {
            var matches = FindMatches(step.Text);
            if (matches.Count == 1)
                return matches[0];

            var message = matches.Count == 0
                ? $"No step binding for: {step}. Add a [Step] method to a class in Steps/."
                : $"{matches.Count} step bindings match: {step}\n  " +
                  string.Join("\n  ", matches.Select(binding => binding.Describe()));
            throw new StepBindingException(message);
        }

        private static List<StepBinding> FindBindings()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.DeclaredOnly;
            return typeof(StepRegistry).Assembly.GetTypes()
                .Where(type => type.IsClass && !type.IsAbstract)
                .SelectMany(type => type.GetMethods(flags))
                .SelectMany(method => method.GetCustomAttributes<StepAttribute>()
                    .Select(attribute => new StepBinding(attribute, method)))
                .ToList();
        }
    }
}
