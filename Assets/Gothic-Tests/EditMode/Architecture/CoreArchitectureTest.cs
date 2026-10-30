using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core;
using NUnit.Framework;
using UnityEngine;

namespace Gothic.Tests.EditMode.Architecture
{
    /// <summary>
    /// Guards the layer model of Gothic.Core: Models <- Domain/Services <- Adapters (see Docs/architecture.md).
    ///
    /// Existing violations are listed in ArchitectureBaseline.txt and tolerated. The baseline may only shrink.
    /// The tests fail on
    /// 1. new violations - fix the code by moving it to the right layer.
    /// 2. fixed violations still in the baseline - remove the line, so the violation can't come back.
    ///
    /// Layer and namespace rules also run without Unity (.github/architecture-check), locally or as a manual workflow.
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
            var (added, resolved) =
                CoreLayerScanner.CompareWithBaseline(rule, violations, File.ReadAllLines(_baselinePath));

            if (added.Any())
            {
                Assert.Fail($"{ruleDescription}\n" +
                            "New violations found. Move the code to the right layer (see Docs/architecture.md). " +
                            "The baseline may only shrink, don't add these lines to it:\n" +
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
