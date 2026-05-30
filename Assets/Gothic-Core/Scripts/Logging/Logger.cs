using System.Diagnostics;
using System.Linq;
using UberLogger;
using UnityEngine;
using ZenKit;

namespace GUZ.Core.Logging
{
    /// <summary>
    /// Hint: Unity ignores StackTrace elements only if:
    /// * Class is named *Logger
    /// * Method is named Log*()
    ///
    /// Therefore, we use Logger.Log*() for all log methods.
    /// For UberLogger to ignore elements in StackTrace, we also use [StackTraceIgnore]
    /// </summary>
    public static class Logger
    {
        [StackTraceIgnore]
        [HideInCallstack]
        public static void Log(string message, LogCat cat)
        {
            UberDebug.LogChannel(cat.ToString(), message);
        }

        [StackTraceIgnore]
        [HideInCallstack]
        public static void LogWarning(string message, LogCat cat)
        {
            UberDebug.LogWarningChannel(cat.ToString(), message);
        }

        [StackTraceIgnore]
        [HideInCallstack]
        public static void LogError(string message, LogCat cat)
        {
            UberDebug.LogErrorChannel(cat.ToString(), message);
        }


        [StackTraceIgnore]
        [HideInCallstack]
        [Conditional("UNITY_EDITOR")]
        public static void LogEditor(string message, LogCat cat)
        {
            Log(message, cat);
        }

        [StackTraceIgnore]
        [HideInCallstack]
        [Conditional("UNITY_EDITOR")]
        public static void LogWarningEditor(string message, LogCat cat)
        {
            LogWarning(message, cat);
        }

        [StackTraceIgnore]
        [HideInCallstack]
        [Conditional("UNITY_EDITOR")]
        public static void LogErrorEditor(string message, LogCat cat)
        {
            LogError(message, cat);
        }



        // Too spammy by default!
        private static string[] _ignoredLogWarningMessages = new[] { "Object not fully loaded: zCCSAtomicBlock" };

        public static void OnZenKitLogMessage(LogLevel level, string name, string message)
        {
            // Using the fastest string concatenation as we might have a lot of logs here.
            var messageString = string.Concat("level=", level, ", name=", name, ", message=", message);

            switch (level)
            {
                case LogLevel.Error:
                    LogError(messageString, LogCat.ZenKit);
                    break;
                case LogLevel.Warning:
                    if (_ignoredLogWarningMessages.Contains(message))
                    {
                        break;
                    }
                    LogWarning(messageString, LogCat.ZenKit);
                    break;
                case LogLevel.Info:
                case LogLevel.Debug:
                case LogLevel.Trace:
                    Log(messageString, LogCat.ZenKit);
                    break;
            }
        }

        public static void OnDirectMusicLogMessage(DirectMusic.LogLevel level, string message)
        {
            // Using the fastest string concatenation as we might have a lot of logs here.
            var messageString = string.Concat("level=", level, ", message=", message);

            switch (level)
            {
                case DirectMusic.LogLevel.Error:
                    LogError(messageString, LogCat.Audio);
                    break;
                case DirectMusic.LogLevel.Warning:
                    if (message.EndsWith("bytes remaining")) // Spammy
                        break;
                    LogWarningEditor(messageString, LogCat.Audio);
                    break;
                case DirectMusic.LogLevel.Info:
                case DirectMusic.LogLevel.Debug:
                case DirectMusic.LogLevel.Trace:
                    Log(messageString, LogCat.Audio);
                    break;
            }
        }
    }
}
