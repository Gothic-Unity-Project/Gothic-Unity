using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// One scenario of a feature file, ready to run: an outline's example row is a scenario of its own. The Test Runner
    /// shows it as "&lt;feature file&gt; / &lt;scenario&gt;".
    /// </summary>
    public class ScenarioCase
    {
        public readonly string FeatureFile;
        public readonly string FeatureName;
        public readonly string Name;
        public readonly int Line;
        public readonly IReadOnlyList<string> Tags;
        public readonly IReadOnlyList<ScenarioStep> Steps;

        /// <summary>
        /// The world its "the world ... is loaded" step names, null without one.
        /// </summary>
        public readonly string World;

        /// <summary>
        /// Set instead of steps when the feature file doesn't parse - the scenario then fails with it.
        /// </summary>
        public readonly string ParseError;

        public string Topic => Path.GetFileNameWithoutExtension(FeatureFile);
        public string TestName => $"{Topic} / {Name}";

        public ScenarioCase(string featureFile, string featureName, string name, int line, IReadOnlyList<string> tags,
            IReadOnlyList<ScenarioStep> steps, string world)
        {
            FeatureFile = featureFile;
            FeatureName = featureName;
            Name = name;
            Line = line;
            Tags = tags;
            Steps = steps;
            World = world;
        }

        public ScenarioCase(string featureFile, string parseError)
        {
            FeatureFile = featureFile;
            FeatureName = featureFile;
            Name = "the feature file parses";
            Tags = Array.Empty<string>();
            Steps = Array.Empty<ScenarioStep>();
            ParseError = parseError;
        }

        /// <summary>
        /// Tag without its '@', e.g. "slow". Tags with a value ("area(world)") match by their name ("area").
        /// </summary>
        public bool HasTag(string tag)
        {
            return Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase) ||
                                 t.StartsWith(tag + "(", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The value of a tag like "timeout(90)", null if the scenario doesn't have it.
        /// </summary>
        public string GetTagValue(string tag)
        {
            var prefix = tag + "(";
            var found = Tags.FirstOrDefault(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                                 t.EndsWith(")"));
            return found?.Substring(prefix.Length, found.Length - prefix.Length - 1);
        }

        public override string ToString()
        {
            return TestName;
        }
    }
}
