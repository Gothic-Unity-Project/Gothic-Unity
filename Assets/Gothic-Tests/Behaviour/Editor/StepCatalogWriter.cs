using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Gothic.Tests.Behaviour.Runner;
using UnityEngine;

namespace Gothic.Tests.Behaviour.Editor
{
    /// <summary>
    /// The step catalog of Docs/behaviour-tests.md, generated from the [Step] attributes so the doc can't drift from
    /// the code (FeatureCatalogTest compares them). Written between two marker lines; the rest of the doc is by hand.
    /// </summary>
    public static class StepCatalogWriter
    {
        public const string StartMarker =
            "<!-- step catalog: written by Gothic > Tests > Write step catalog - don't edit by hand -->";
        public const string EndMarker = "<!-- end of step catalog -->";

        public static readonly string DocPath =
            Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Docs", "behaviour-tests.md");

        // Pattern parts and how the catalog shows them, in this order.
        private static readonly (Regex Pattern, string Readable)[] _readable =
        {
            (new Regex(@"\(\?:a\|an\)"), "a/an"),
            (new Regex(@"\(\\d\+\(\?:\\\.\\d\+\)\?\)"), "<number>"),
            (new Regex(@"\(\\d\{1,2\}\):\(\\d\{2\}\)"), "<hh>:<mm>"),
            (new Regex(@"\(\\d\+\)"), "<n>"),
            (new Regex(@"\\d\+"), "<n>"),
            (new Regex(@"\(\.\*\)"), "<text>"),
            (new Regex(@"\(\?:es\)\?"), "(es)"),
            (new Regex(@"([a-z])s\?"), "$1(s)"),
            (new Regex(@"\\(.)"), "$1")
        };

        /// <summary>
        /// The catalog as markdown: a table per step folder (Steps/&lt;Area&gt;), the step's sentence and its method.
        /// </summary>
        public static string Build()
        {
            var text = new StringBuilder();
            text.Append("Placeholders: `<text>` any text (in quotes in the step), `<n>` a whole number, `<number>` a " +
                "number like 1.5, `(a|b)` one of the words. Keywords (Given, When, Then, And, But) don't matter.\n");

            var areas = StepRegistry.Bindings
                .GroupBy(binding => binding.Method.DeclaringType!.Namespace!.Split('.').Last())
                .OrderBy(area => area.Key, StringComparer.Ordinal);
            foreach (var area in areas)
            {
                text.Append($"\n#### {area.Key}\n\n| Step | Method |\n|---|---|\n");
                foreach (var binding in area.OrderBy(binding => ToReadable(binding.Pattern), StringComparer.Ordinal))
                {
                    var reserved = binding.IsReserved ? " (reserved)" : string.Empty;
                    text.Append($"| `{ToReadable(binding.Pattern).Replace("|", "\\|")}` | " +
                        $"{binding.Method.DeclaringType.Name}.{binding.Method.Name}{reserved} |\n");
                }
            }

            return text.ToString();
        }

        public static string ToReadable(string pattern)
        {
            return _readable.Aggregate(pattern, (text, rule) => rule.Pattern.Replace(text, rule.Readable));
        }

        /// <summary>
        /// The catalog in the doc as it is now, null if the doc or its markers are missing.
        /// </summary>
        public static string ReadFromDoc()
        {
            if (!File.Exists(DocPath))
                return null;

            var doc = File.ReadAllText(DocPath).Replace("\r\n", "\n");
            var start = doc.IndexOf(StartMarker, StringComparison.Ordinal);
            var end = doc.IndexOf(EndMarker, StringComparison.Ordinal);
            if (start < 0 || end < start)
                return null;

            start += StartMarker.Length + 1;
            return doc.Substring(start, end - start).Trim('\n') + "\n";
        }

        /// <summary>
        /// Replaces the catalog between the markers of Docs/behaviour-tests.md.
        /// </summary>
        public static void WriteIntoDoc()
        {
            if (!File.Exists(DocPath))
                throw new FileNotFoundException("No behaviour test doc", DocPath);

            var doc = File.ReadAllText(DocPath).Replace("\r\n", "\n");
            var start = doc.IndexOf(StartMarker, StringComparison.Ordinal);
            var end = doc.IndexOf(EndMarker, StringComparison.Ordinal);
            if (start < 0 || end < start)
                throw new InvalidOperationException($"{DocPath} has no lines \"{StartMarker}\" and \"{EndMarker}\"");

            var before = doc[..(start + StartMarker.Length)];
            File.WriteAllText(DocPath, $"{before}\n\n{Build()}\n{doc[end..]}");
        }
    }
}
