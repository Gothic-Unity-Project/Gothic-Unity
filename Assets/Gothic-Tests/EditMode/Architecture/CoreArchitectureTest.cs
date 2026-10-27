using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core;
using NUnit.Framework;
using UnityEngine;

namespace Gothic.Tests.EditMode.Architecture
{
    /// <summary>
    /// Guards the layer model of Gothic.Core: Models <- Domain/Services <- Adapters (see CLAUDE.md > Architecture).
    ///
    /// Existing violations are listed in ArchitectureBaseline.txt and tolerated. The tests fail on
    /// 1. new violations - fix the code, or, if it's a reviewed exception, add the line to the baseline.
    /// 2. fixed violations still in the baseline - remove the line, so the violation can't come back.
    /// </summary>
    public class CoreArchitectureTest
    {
        private static readonly string _coreScriptsPath = Path.Combine(Application.dataPath, "Gothic-Core", "Scripts");
        private static readonly string _baselinePath =
            Path.Combine(Application.dataPath, "Gothic-Tests", "EditMode", "Architecture", "ArchitectureBaseline.txt");

        private CoreLayerScanner _scanner;


        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _scanner = new CoreLayerScanner(_coreScriptsPath);
        }

        [Test]
        public void LayersOnlyUseOwnOrLowerLayers()
        {
            AssertMatchesBaseline(CoreLayerScanner.RuleLayer, _scanner.FindLayerViolations(),
                "A layer may only use namespaces of its own or lower layers (Models <- Domain/Services <- Adapters).");
        }

        [Test]
        public void NamespacesStartWithTheirLayer()
        {
            AssertMatchesBaseline(CoreLayerScanner.RuleNamespace, _scanner.FindNamespaceViolations(),
                "Namespaces must start with Gothic.Core.{Layer} of the folder the file lives in.");
        }

        [Test]
        public void MonoBehavioursOnlyLiveInAdapters()
        {
            var adapterLayer = CoreLayerScanner.Layers.Length - 1;
            var violations = typeof(GlobalEventDispatcher).Assembly.GetTypes()
                .Where(type => typeof(MonoBehaviour).IsAssignableFrom(type))
                .Select(type => (Type: type, Layer: _scanner.GetLayerOfNamespace(type.Namespace)))
                .Where(entry => entry.Layer >= 0 && entry.Layer != adapterLayer)
                .Select(entry => CoreLayerScanner.Format(CoreLayerScanner.RuleMonoBehaviour, entry.Type.FullName,
                    CoreLayerScanner.Layers[entry.Layer]))
                .OrderBy(violation => violation)
                .ToList();

            AssertMatchesBaseline(CoreLayerScanner.RuleMonoBehaviour, violations,
                "MonoBehaviours are Adapters. Move Unity lifecycle code to an Adapter and logic to a Service/Domain.");
        }

        private static void AssertMatchesBaseline(string rule, List<string> violations, string ruleDescription)
        {
            var baseline = new HashSet<string>(File.ReadAllLines(_baselinePath)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(rule + " |")));

            var added = violations.Where(violation => !baseline.Contains(violation)).ToList();
            var resolved = baseline.Where(entry => !violations.Contains(entry)).OrderBy(entry => entry).ToList();

            if (added.Any())
            {
                Assert.Fail($"{ruleDescription}\n" +
                            "New violations found. Move the code to the right layer. Only if this is a reviewed, " +
                            $"intended exception, add these lines to {_baselinePath}:\n" +
                            string.Join("\n", added));
            }

            if (resolved.Any())
            {
                Assert.Fail("These violations are fixed. Remove the lines from the baseline, so they can't come back " +
                            $"({_baselinePath}):\n" + string.Join("\n", resolved));
            }
        }
    }
}
