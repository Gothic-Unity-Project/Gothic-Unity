using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Gothic.Tests.EditMode.Architecture
{
    /// <summary>
    /// Source based scan of Gothic.Core's layer folders. Layers are taken from the folder a file lives in, not from its
    /// namespace, as some namespaces don't match their folder (e.g. Gothic.Core.Manager inside Services/).
    ///
    /// Each violation is a single line "rule | file-or-type | detail", which is also the format of the baseline file.
    /// No UnityEngine dependency on purpose, so it can be run outside of Unity as well.
    /// </summary>
    public class CoreLayerScanner
    {
        public const string RuleLayer = "layer";
        public const string RuleNamespace = "namespace";
        public const string RuleMonoBehaviour = "monobehaviour";

        /// <summary>
        /// Layer folders, lowest to highest.
        /// </summary>
        public static readonly string[] Layers = { "Models", "Domain", "Services", "Adapters" };

        /// <summary>
        /// Rank per entry of Layers. A layer may only use namespaces of its own or lower ranks.
        /// Domain and Services share a rank: Domain classes are workers owned by a Service and may use Services.
        /// </summary>
        private static readonly int[] _ranks = { 0, 1, 1, 2 };

        private static readonly Regex _namespaceRegex = new(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);

        private static readonly Regex _usingRegex =
            new(@"^\s*using\s+(?:static\s+)?(?:\w+\s*=\s*)?([\w.]+)\s*;", RegexOptions.Multiline);

        private readonly string _coreScriptsPath;
        private readonly List<(string Path, int Layer, string Content)> _files = new();
        private readonly Dictionary<string, int> _namespaceLayers = new();


        public CoreLayerScanner(string coreScriptsPath)
        {
            _coreScriptsPath = coreScriptsPath;

            for (var layer = 0; layer < Layers.Length; layer++)
            {
                var layerPath = Path.Combine(coreScriptsPath, Layers[layer]);
                foreach (var file in Directory.GetFiles(layerPath, "*.cs", SearchOption.AllDirectories))
                {
                    var content = File.ReadAllText(file);
                    _files.Add((ToRelativePath(file), layer, content));

                    foreach (Match match in _namespaceRegex.Matches(content))
                        _namespaceLayers[match.Groups[1].Value] = layer;
                }
            }
        }

        /// <summary>
        /// Layer index of a namespace, based on the folder it's declared in. Sub namespaces inherit from their parent.
        /// Returns -1 if the namespace isn't declared within the layer folders (e.g. ZenKit, UnityEngine, Gothic.Core).
        /// </summary>
        public int GetLayerOfNamespace(string nameSpace)
        {
            if (nameSpace == null)
                return -1;

            var bestMatch = string.Empty;
            var bestLayer = -1;
            foreach (var (declared, layer) in _namespaceLayers)
            {
                if (declared.Length <= bestMatch.Length)
                    continue;
                if (nameSpace != declared && !nameSpace.StartsWith(declared + "."))
                    continue;

                bestMatch = declared;
                bestLayer = layer;
            }

            return bestLayer;
        }

        /// <summary>
        /// A file must not have a using of a namespace living in a higher ranked layer.
        /// </summary>
        public List<string> FindLayerViolations()
        {
            var violations = new List<string>();
            foreach (var (path, layer, content) in _files)
            {
                foreach (Match match in _usingRegex.Matches(content))
                {
                    var usedNamespace = match.Groups[1].Value;
                    var usedLayer = GetLayerOfNamespace(usedNamespace);
                    if (usedLayer >= 0 && _ranks[usedLayer] > _ranks[layer])
                        violations.Add(Format(RuleLayer, path, usedNamespace));
                }
            }

            return Sorted(violations);
        }

        /// <summary>
        /// Every namespace of a file must start with Gothic.Core.{Layer}, so that the layer is visible from the code.
        /// </summary>
        public List<string> FindNamespaceViolations()
        {
            var violations = new List<string>();
            foreach (var (path, layer, content) in _files)
            {
                var expected = $"Gothic.Core.{Layers[layer]}";
                var matches = _namespaceRegex.Matches(content);

                if (matches.Count == 0)
                    violations.Add(Format(RuleNamespace, path, "<global>"));

                foreach (Match match in matches)
                {
                    var nameSpace = match.Groups[1].Value;
                    if (nameSpace != expected && !nameSpace.StartsWith(expected + "."))
                        violations.Add(Format(RuleNamespace, path, nameSpace));
                }
            }

            return Sorted(violations);
        }

        /// <summary>
        /// Violations of one rule which aren't in the baseline yet (Added), and baseline lines of that rule which are no
        /// violation anymore (Resolved). Both have to be empty, as the baseline may only shrink.
        /// </summary>
        public static (List<string> Added, List<string> Resolved) CompareWithBaseline(string rule,
            List<string> violations, IEnumerable<string> baselineLines)
        {
            var baseline = new HashSet<string>(baselineLines
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(rule + " |")));

            var added = violations.Where(violation => !baseline.Contains(violation)).ToList();
            var resolved = Sorted(baseline.Where(entry => !violations.Contains(entry)));

            return (added, resolved);
        }

        public static string Format(string rule, string subject, string detail)
        {
            return $"{rule} | {subject} | {detail}";
        }

        private string ToRelativePath(string file)
        {
            return Path.GetRelativePath(_coreScriptsPath, file).Replace('\\', '/');
        }

        private static List<string> Sorted(IEnumerable<string> violations)
        {
            return violations.Distinct().OrderBy(v => v, StringComparer.Ordinal).ToList();
        }
    }
}
