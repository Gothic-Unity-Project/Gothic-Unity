using Gothic.Core.Models.Config;
#if !UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UberLogger;
using UnityEngine;
using ILogger = UberLogger.ILogger;
using Object = UnityEngine.Object;
#endif

namespace Gothic.Core.Logging
{
    public class FileLoggingHandler
    {
#if UNITY_EDITOR
        public void Init(JsonRootConfig _)
        {
            // Disabled in Editor.
        }

        public void Destroy()
        {
            // Disabled in Editor.
        }
#else
        private FileLoggingLogger _logger;

        // One file per session (a run of the build), named after its start in local time, so a stream's VOD can be
        // matched to it line by line. The newest ones are kept.
        private const string _logFolderName = "Logs";
        private const string _logFilePrefix = "Gothic-Unity_";
        private const string _logFileSuffix = ".log.txt";
        private const int _keptSessionLogs = 30;

        public FileLoggingHandler()
        {
            // As we need to get logs as early as possible, we will pre-initialize it immediately.
            PreInit();
        }

        private void PreInit()
        {
            var sessionStart = DateTime.Now;
            var logFolder = Path.Combine(Application.persistentDataPath, _logFolderName);
            Directory.CreateDirectory(logFolder);
            DeleteOldSessionLogs(logFolder);
            var logFilePath = Path.Combine(logFolder,
                $"{_logFilePrefix}{sessionStart:yyyy-MM-dd_HH-mm-ss}{_logFileSuffix}");

            _logger = new FileLoggingLogger(logFilePath, false, sessionStart.ToUniversalTime());
            UberLogger.Logger.AddLogger(_logger);

            _logger.WriteLine($"Session start: {sessionStart:yyyy-MM-dd HH:mm:ss} (every line starts with the local " +
                              "time, then the seconds since the start)");
            _logger.WriteLine("DeviceModel: " + SystemInfo.deviceModel);
            _logger.WriteLine("DeviceType: " + SystemInfo.deviceType);
            _logger.WriteLine("OperatingSystem: " + SystemInfo.operatingSystem);
            _logger.WriteLine("OperatingSystemFamily: " + SystemInfo.operatingSystemFamily);
            _logger.WriteLine("MemorySize: " + SystemInfo.systemMemorySize);
            _logger.WriteLine("Gothic Unity Version: " + Application.version);
            _logger.WriteLine(string.Empty);
        }

        /// <summary>
        /// Keeps the newest session logs only (the names sort by date).
        /// </summary>
        private static void DeleteOldSessionLogs(string logFolder)
        {
            try
            {
                var oldLogs = Directory.GetFiles(logFolder, $"{_logFilePrefix}*{_logFileSuffix}")
                    .OrderByDescending(Path.GetFileName)
                    .Skip(_keptSessionLogs - 1);
                foreach (var oldLog in oldLogs)
                    File.Delete(oldLog);
            }
            catch (Exception e)
            {
                // Logging isn't set up yet - a log that can't be deleted just stays.
                Debug.LogWarning($"Old session logs couldn't be deleted: {e.Message}");
            }
        }

        public void Init(JsonRootConfig rootConfig)
        {
            LogSeverity logLevel;

            if (Enum.TryParse(rootConfig.LogLevel, true, out LogSeverity value))
            {
                _logger.WriteLine("LogLevel Setting found: " + rootConfig.LogLevel);
                logLevel = value;
            }
            else
            {
                _logger.WriteLine("LogLevel Setting not found. Setting Default to >Warning<.");
                logLevel = LogSeverity.Warning;
            }

            string[] logCategories;
            if (rootConfig.LogCategories != null)
            {
                logCategories = rootConfig.LogCategories.Split(',', ';')
                    // We sanitize possible case issues as we want to have string channels for a small performance benefit when logging at runtime.
                    .Select(i => Enum.TryParse(i.Trim(), true, out LogCat cat) ? cat.ToString() : null)
                    .Where(i => i != null)
                    .ToArray();
            }
            else
            {
                logCategories = Array.Empty<string>();
            }

            if (logCategories.Length == 0)
            {
                var enumValues = Enum.GetValues(typeof(LogCat)).Cast<LogCat>().Select(v => v.ToString());
                _logger.WriteLine($"No valid category found. Activating logs for all categories: [{string.Join(';', enumValues)}]");
            }
            else
            {
                _logger.WriteLine($"Log Categories found and activated: [{string.Join(';', logCategories)}]");
            }
            _logger.WriteLine(string.Empty);

            UberLogger.Logger.AddFilter(new FileLoggingFilter(logLevel, logCategories));
        }

        public void Destroy()
        {
            _logger = null;
        }


        private class FileLoggingLogger : ILogger
        {
            private readonly StreamWriter _logFileWriter;
            private readonly bool _includeCallStacks;
            // Seconds since the start from UTC: UberLogger's relative time is local (DateTime.Now) and goes negative
            // when the clock goes back an hour (daylight saving time ending mid-session).
            private readonly DateTime _sessionStartUtc;

            public FileLoggingLogger(string fileLogPath, bool includeCallStacks, DateTime sessionStartUtc)
            {
                _includeCallStacks = includeCallStacks;
                _sessionStartUtc = sessionStartUtc;
                _logFileWriter = new StreamWriter(fileLogPath, false);
                _logFileWriter.AutoFlush = true;
            }

            public void Log(LogInfo logInfo)
            {
                lock(this)
                {
                    // <local time> (<seconds since the start>) <severity> [<category>] <message>
                    var fullMessage = string.Format(CultureInfo.InvariantCulture,
                        "{0:HH:mm:ss.fff} ({1:0.0000}) {2} [{3}] {4}",
                        logInfo.AbsoluteTimeStamp.ToLocalTime(),
                        (logInfo.AbsoluteTimeStamp - _sessionStartUtc).TotalSeconds,
                        logInfo.Severity,
                        logInfo.Channel,
                        logInfo.Message);

                    _logFileWriter.WriteLine(fullMessage);

                    if(_includeCallStacks && logInfo.Callstack.Count>0)
                    {
                        foreach(var frame in logInfo.Callstack)
                        {
                            _logFileWriter.WriteLine(frame.GetFormattedMethodNameWithFileName());
                        }
                        _logFileWriter.WriteLine();
                    }
                }
            }

            /// <summary>
            /// Write a single line without additional formatting or checks.
            /// </summary>
            public void WriteLine(string message)
            {
                lock(this)
                {
                    _logFileWriter.WriteLine(message);
                }
            }

            ~FileLoggingLogger()
            {
                lock (this)
                {
                    _logFileWriter.Close();
                }
            }
        }

        private class FileLoggingFilter : IFilter
        {
            private readonly LogSeverity _logLevel;
            private readonly string[] _logCategories;


            public FileLoggingFilter(LogSeverity logLevel, string[] logCategories)
            {
                _logLevel = logLevel;
                _logCategories = logCategories;
            }

            public bool ApplyFilter(string channel, Object source, LogSeverity severity, object message, params object[] par)
            {
                return severity >= _logLevel
                       && (_logCategories.Length == 0 || _logCategories.Contains(channel));
            }
        }
#endif
    }
}
