using GUZ.Core.Models.Config;
#if !UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UberLogger;
using UnityEngine;
using ILogger = UberLogger.ILogger;
using Object = UnityEngine.Object;
#endif

namespace GUZ.Core.Logging
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

        private const string _logFileName = "Gothic-UnZENity.log.txt";
        
        public FileLoggingHandler()
        {
            // As we need to get logs as early as possible, we will pre-initialize it immediately.
            PreInit();
        }

        private void PreInit()
        {
            string rootFolder;
            if (Application.platform == RuntimePlatform.Android)
            {
                rootFolder = Application.persistentDataPath;
            }
            else
            {
                rootFolder = Application.persistentDataPath;
            }

            _logger = new FileLoggingLogger($"{rootFolder}/{_logFileName}", false);
            UberLogger.Logger.AddLogger(_logger);

            _logger.WriteLine("DeviceModel: " + SystemInfo.deviceModel);
            _logger.WriteLine("DeviceType: " + SystemInfo.deviceType);
            _logger.WriteLine("OperatingSystem: " + SystemInfo.operatingSystem);
            _logger.WriteLine("OperatingSystemFamily: " + SystemInfo.operatingSystemFamily);
            _logger.WriteLine("MemorySize: " + SystemInfo.systemMemorySize);
            _logger.WriteLine("Gothic-UnZENity Version: " + Application.version);
            _logger.WriteLine(string.Empty);
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

            public FileLoggingLogger(string fileLogPath, bool includeCallStacks)
            {
                _includeCallStacks = includeCallStacks;
                _logFileWriter = new StreamWriter(fileLogPath, false);
                _logFileWriter.AutoFlush = true;
            }

            public void Log(LogInfo logInfo)
            {
                lock(this)
                {
                    // RFC 5424: <timestamp> <severity> [<category>] <message>
                    var fullMessage = string.Format("{0} {1} [{2}] {3}",
                        logInfo.GetRelativeTimeStampAsString(),
                        logInfo.Severity,
                        logInfo.Channel,
                        logInfo.Message
                    );

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
