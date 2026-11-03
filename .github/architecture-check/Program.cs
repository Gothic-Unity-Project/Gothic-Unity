using System;
using System.IO;
using Gothic.Tests.EditMode.Architecture;

namespace Gothic.ArchitectureCheck
{
    /// <summary>
    /// CI version of CoreArchitectureTest's layer and namespace rules. Argument: repository root (default: current dir).
    /// Exit code 1 if a violation is new or a fixed one is still in the baseline.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            var coreScriptsPath = Path.Combine(root, "Assets", "Gothic-Core", "Scripts");
            var baselinePath = Path.Combine(root, "Assets", "Gothic-Tests", "EditMode", "Architecture",
                "ArchitectureBaseline.txt");

            var scanner = new CoreLayerScanner(coreScriptsPath);
            var baseline = File.ReadAllLines(baselinePath);

            var failed = Check(CoreLayerScanner.RuleLayer, scanner, baseline);
            failed |= Check(CoreLayerScanner.RuleNamespace, scanner, baseline);

            if (failed)
            {
                Console.WriteLine();
                Console.WriteLine("See Docs/architecture.md > Layers inside Core. The baseline may only shrink.");
                return 1;
            }

            Console.WriteLine("Architecture check passed.");
            return 0;
        }

        private static bool Check(string rule, CoreLayerScanner scanner, string[] baseline)
        {
            var violations = rule == CoreLayerScanner.RuleLayer
                ? scanner.FindLayerViolations()
                : scanner.FindNamespaceViolations();
            var (added, resolved) = CoreLayerScanner.CompareWithBaseline(rule, violations, baseline);

            foreach (var violation in added)
                Console.WriteLine($"::error title=New architecture violation::{violation}");
            foreach (var entry in resolved)
                Console.WriteLine($"::error title=Fixed violation still in baseline, remove the line::{entry}");

            return added.Count > 0 || resolved.Count > 0;
        }
    }
}
