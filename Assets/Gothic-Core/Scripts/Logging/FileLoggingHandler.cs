using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Gothic.Core.Models.Config;
using UberLogger;
using UnityEngine;
using ILogger = UberLogger.ILogger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Logging
{
    public class FileLoggingHandler
    {
        private const string _legacyLogFileName = "Gothic-Unity.log.txt";

        // DeveloperConfig.EnableSessionLogFiles: one file per session (a build's run, an Editor play mode), named after
        // its start (local time) - a stream's VOD can be matched to it line by line.
        private const string _logFolderName = "Logs";
        private const string _logFilePrefix = "Gothic-Unity_";
        private const string _logFileSuffix = ".log.txt";
        private const int _keptSessionLogs = 30;

        private readonly bool _isSessionLog;
        private FileLoggingLogger _logger;

        public FileLoggingHandler(DeveloperConfig developerConfig)
        {
            _isSessionLog = developerConfig != null && developerConfig.EnableSessionLogFiles;

            // The Editor has its console and Editor.log - only session logs are written there.
            if (Application.isEditor && !_isSessionLog)
                return;

            // As we need to get logs as early as possible, we will pre-initialize it immediately.
            PreInit();
        }

        private void PreInit()
        {
            var rootFolder = Application.persistentDataPath;

            string logFilePath;
            var sessionStart = DateTime.Now;
            if (_isSessionLog)
            {
                var logFolder = Path.Combine(rootFolder, _logFolderName);
                Directory.CreateDirectory(logFolder);
                DeleteOldSessionLogs(logFolder);
                logFilePath = Path.Combine(logFolder,
                    $"{_logFilePrefix}{sessionStart:yyyy-MM-dd_HH-mm-ss}{_logFileSuffix}");
            }
            else
            {
                logFilePath = Path.Combine(rootFolder, _legacyLogFileName);
            }

            _logger = new FileLoggingLogger(logFilePath, false, _isSessionLog, sessionStart.ToUniversalTime());
            UberLogger.Logger.AddLogger(_logger);

            if (_isSessionLog)
                _logger.WriteLine($"Session start: {sessionStart:yyyy-MM-dd HH:mm:ss} " +
                                  $"({(Application.isEditor ? "Editor" : "build")}; every line starts with the local " +
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
                    .OrderByDescending(path => Path.GetFileName(path))
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
            if (_logger == null)
                return;

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

            // UberLogger's filters silence the Unity console too - in the Editor only the file is filtered.
            var filter = new FileLoggingFilter(logLevel, logCategories);
            if (Application.isEditor)
                _logger.Filter = filter;
            else
                UberLogger.Logger.AddFilter(filter);
        }

        public void Destroy()
        {
            if (_logger == null)
                return;

            // The Editor keeps UberLogger's static loggers without a domain reload - the next play mode adds its own.
            UberLogger.Logger.RemoveLogger(typeof(FileLoggingLogger).FullName);
            _logger.Close();
            _logger = null;
        }


        private class FileLoggingLogger : ILogger
        {
            private readonly StreamWriter _logFileWriter;
            private readonly bool _includeCallStacks;
            private readonly bool _isLocalTimeShown;
            // Seconds since the start from UTC: UberLogger's relative time is local (DateTime.Now) and went negative
            // when the clock went back an hour (daylight saving time ended mid-session).
            private readonly DateTime _sessionStartUtc;

            // The Editor's own filter (in builds UberLogger filters globally).
            public IFilter Filter;

            public FileLoggingLogger(string fileLogPath, bool includeCallStacks, bool isLocalTimeShown,
                DateTime sessionStartUtc)
            {
                _includeCallStacks = includeCallStacks;
                _isLocalTimeShown = isLocalTimeShown;
                _sessionStartUtc = sessionStartUtc;
                _logFileWriter = new StreamWriter(fileLogPath, false);
                _logFileWriter.AutoFlush = true;
            }

            public void Log(LogInfo logInfo)
            {
                if (Filter != null && !Filter.ApplyFilter(logInfo.Channel, null, logInfo.Severity, logInfo.Message))
                    return;

                lock(this)
                {
                    // RFC 5424: <timestamp> <severity> [<category>] <message> - session logs start with the local time
                    // (VOD timestamps).
                    var fullMessage = _isLocalTimeShown
                        ? string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss.fff} ({1:0.0000}) {2} [{3}] {4}",
                            logInfo.AbsoluteTimeStamp.ToLocalTime(),
                            (logInfo.AbsoluteTimeStamp - _sessionStartUtc).TotalSeconds,
                            logInfo.Severity,
                            logInfo.Channel,
                            logInfo.Message)
                        : string.Format("{0} {1} [{2}] {3}",
                            logInfo.GetRelativeTimeStampAsString(),
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

            public void Close()
            {
                lock (this)
                {
                    _logFileWriter.Close();
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
    }
}
