using System;
using System.IO;
using UnityEngine;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// The commit a run was produced from. Part of the run directory name and of manifest.json, so an artifact
    /// bundle downloaded from CI weeks later can still be tied back to the code which produced it. (ADR-0001 §3.7)
    ///
    /// Resolved by reading .git directly rather than by starting a git process: the harness runs inside a
    /// PlayMode test, where spawning a child process is both slow and one more thing which can hang a CI run.
    /// </summary>
    public static class GitRevision
    {
        /// <summary>
        /// GitHub Actions checks out a detached HEAD, but it also hands us the SHA directly - and its value is
        /// the one the CI UI shows, so it wins over anything we could read from the working copy.
        /// </summary>
        private const string _ciEnvironmentVariable = "GITHUB_SHA";

        public const string Unknown = "unknown";

        private static string _current;


        /// <summary>
        /// Full SHA, or <see cref="Unknown"/> when this is not a git working copy (i.e. a standalone player).
        /// </summary>
        public static string Current => _current ??= Resolve();

        /// <summary>
        /// Short form used wherever a SHA has to stay readable, i.e. in the run directory name.
        /// </summary>
        public static string CurrentShort
        {
            get
            {
                var current = Current;
                return current.Length <= 8 ? current : current.Substring(0, 8);
            }
        }


        private static string Resolve()
        {
            var fromCi = Environment.GetEnvironmentVariable(_ciEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromCi))
                return fromCi.Trim();

            try
            {
                var gitDirectory = Path.Combine(HarnessPaths.ProjectRoot, ".git");

                if (!Directory.Exists(gitDirectory))
                    return Unknown;

                var head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();

                // Detached HEAD stores the SHA itself, an attached one stores >ref: refs/heads/main<.
                if (!head.StartsWith("ref:", StringComparison.Ordinal))
                    return head;

                var reference = head.Substring("ref:".Length).Trim();
                var referenceFile = Path.Combine(gitDirectory, reference.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(referenceFile))
                    return File.ReadAllText(referenceFile).Trim();

                return ResolveFromPackedRefs(gitDirectory, reference);
            }
            catch (Exception exception)
            {
                // A missing SHA degrades a manifest, it must never fail a run.
                Debug.LogWarning($"Could not resolve the git revision: {exception.Message}");
                return Unknown;
            }
        }

        /// <summary>
        /// A branch which was never updated since the clone has no loose ref file; its SHA lives in packed-refs.
        /// </summary>
        private static string ResolveFromPackedRefs(string gitDirectory, string reference)
        {
            var packedRefs = Path.Combine(gitDirectory, "packed-refs");

            if (!File.Exists(packedRefs))
                return Unknown;

            foreach (var line in File.ReadAllLines(packedRefs))
            {
                if (line.Length == 0 || line[0] == '#' || line[0] == '^')
                    continue;

                var separator = line.IndexOf(' ');
                if (separator < 0)
                    continue;

                if (line.Substring(separator + 1).Trim() == reference)
                    return line.Substring(0, separator);
            }

            return Unknown;
        }
    }
}
