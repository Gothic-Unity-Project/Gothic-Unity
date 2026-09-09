using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// The one directory a whole CI invocation writes into, laid out as
    /// >Artifacts/functional/[utc-timestamp]-[git-sha]/[scenario]/<. (ADR-0001 §3.7)
    ///
    /// Resolved once per process, not once per session: a run contains several scenarios and they belong to one
    /// bundle, with a single manifest.json describing all of them. A CI job which wants to name the bundle itself
    /// (i.e. to hand the very same path to an upload step) sets >GOTHIC_TEST_RUN_DIR<.
    /// </summary>
    public static class TestRunDirectory
    {
        public const string OverrideArgument = "-gothicTestRunDir";
        public const string OverrideEnvironmentVariable = "GOTHIC_TEST_RUN_DIR";

        private static readonly Regex _unsafeCharacters = new("[^A-Za-z0-9_.-]+");

        private static string _root;


        /// <summary>
        /// Root of the current run. Created on first access.
        /// </summary>
        public static string Root
        {
            get
            {
                if (_root == null)
                {
                    _root = Resolve();
                    Directory.CreateDirectory(_root);
                }

                return _root;
            }
        }

        /// <summary>
        /// Sub directory of one scenario inside the current run. Created on first access.
        /// </summary>
        public static string ForScenario(string scenarioName)
        {
            var directory = Path.Combine(Root, Sanitize(scenarioName));
            Directory.CreateDirectory(directory);

            return directory;
        }

        /// <summary>
        /// File names end up in artifact bundles, ZIP files and URLs; a scenario name is a C# type name and may
        /// carry characters none of those handle well.
        /// </summary>
        public static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "unnamed";

            return _unsafeCharacters.Replace(name.Trim(), "-");
        }

        private static string Resolve()
        {
            var overridePath = ArgumentReader.Read(OverrideArgument, OverrideEnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(overridePath))
                return overridePath.Trim();

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

            return Path.Combine(HarnessPaths.ArtifactRoot, $"{timestamp}-{GitRevision.CurrentShort}");
        }
    }
}
