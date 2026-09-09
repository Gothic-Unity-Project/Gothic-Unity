using System;
using System.Globalization;
using System.IO;
using Gothic.Core.Logging;
using Gothic.Testing.Harness;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Testing.Recording
{
    /// <summary>
    /// Writes >manifest.json< into the run root. (ADR-0001 §3.7)
    ///
    /// It is rewritten on every session start and end rather than once at the end of the run, because the case it
    /// has to survive is the one where the run does not end normally: a killed process, a crashed Editor, a
    /// runner which ran out of disk. A manifest which only exists for successful runs is a manifest for the runs
    /// nobody needs to debug.
    /// </summary>
    public static class RunManifest
    {
        public const string FileName = "manifest.json";

        private static RunManifestData _data;


        public static string FilePath => Path.Combine(TestRunDirectory.Root, FileName);


        /// <summary>
        /// Add a session and persist it immediately, so the seed is on disk before the scenario does anything
        /// which could take the process down with it.
        /// </summary>
        public static SessionManifestEntry BeginSession(string scenarioName, string sessionDirectory,
            string configName, int seed, float captureFrameRate)
        {
            var entry = new SessionManifestEntry
            {
                Scenario = scenarioName,
                Directory = RelativeToRun(sessionDirectory),
                ConfigName = configName,
                Seed = seed,
                CaptureFrameRate = captureFrameRate,
                StartedUtc = Timestamp()
            };

            Data.Sessions.Add(entry);
            Save();

            return entry;
        }

        public static void EndSession(SessionManifestEntry entry, double durationSeconds,
            double durationRealtimeSeconds, int stepCount)
        {
            if (entry == null)
                return;

            entry.EndedUtc = Timestamp();
            entry.DurationSeconds = durationSeconds;
            entry.DurationRealtimeSeconds = durationRealtimeSeconds;
            entry.StepCount = stepCount;

            Save();
        }

        /// <summary>
        /// Drop the cached manifest. Only needed by tests of the harness itself, which fake a new run inside one
        /// Editor session.
        /// </summary>
        public static void Reset()
        {
            _data = null;
        }

        private static RunManifestData Data => _data ??= Load();

        private static RunManifestData Load()
        {
            // A second fixture in the same run must extend the existing manifest instead of replacing it - NUnit
            // creates a fresh instance per fixture, so in-memory state alone is not enough.
            try
            {
                if (File.Exists(FilePath))
                {
                    var existing = JsonUtility.FromJson<RunManifestData>(File.ReadAllText(FilePath));

                    if (existing != null)
                    {
                        existing.Sessions ??= new();
                        return existing;
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not read >{FilePath}<, starting a new manifest: {exception.Message}",
                    LogCat.Test);
            }

            return Create();
        }

        private static RunManifestData Create()
        {
            return new RunManifestData
            {
                RunId = Path.GetFileName(TestRunDirectory.Root),
                StartedUtc = Timestamp(),
                GitSha = GitRevision.Current,
                UnityVersion = Application.unityVersion,
                ApplicationVersion = Application.version,
                Platform = Application.platform.ToString(),
                Machine = SystemInfo.deviceName,
                OperatingSystem = SystemInfo.operatingSystem,
                ProcessorCount = SystemInfo.processorCount,
                SystemMemorySize = SystemInfo.systemMemorySize,
                GraphicsDevice = SystemInfo.graphicsDeviceName
            };
        }

        private static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception exception)
            {
                // Losing the manifest degrades the bundle; failing the run over it would be worse.
                Logger.LogWarning($"Could not write >{FilePath}<: {exception.Message}", LogCat.Test);
            }
        }

        private static string RelativeToRun(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                return string.Empty;

            var root = TestRunDirectory.Root;

            return directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? directory.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : directory;
        }

        private static string Timestamp()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }
    }
}
