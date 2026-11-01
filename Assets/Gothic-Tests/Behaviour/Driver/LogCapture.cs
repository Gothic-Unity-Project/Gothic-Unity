using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Gothic.Tests.Behaviour.Driver
{
    /// <summary>
    /// The log lines of one scenario (Application.logMessageReceived, every Logger.Log ends there), the last
    /// MaxLines of them. For lines without state behind them (an NPC's overlay text, a reason a service logs);
    /// where state exists, steps check the state.
    /// </summary>
    public class LogCapture
    {
        public const int MaxLines = 500;

        private readonly Queue<(LogType Type, string Text)> _lines = new();
        private bool _isCapturing;

        /// <summary>
        /// Lines received since Start, dropped ones included.
        /// </summary>
        public int Received { get; private set; }

        public void Start()
        {
            if (_isCapturing)
                return;

            _isCapturing = true;
            Application.logMessageReceived += OnLogMessageReceived;
        }

        public void Stop()
        {
            if (!_isCapturing)
                return;

            _isCapturing = false;
            Application.logMessageReceived -= OnLogMessageReceived;
        }

        public int Count(string text)
        {
            return _lines.Count(line => line.Text.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The last lines of a type, e.g. the warnings and errors for a failure message.
        /// </summary>
        public List<string> Last(int count, params LogType[] types)
        {
            return _lines.Where(line => types.Length == 0 || types.Contains(line.Type)).Select(line => line.Text)
                .Reverse().Take(count).Reverse().ToList();
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            Received++;
            _lines.Enqueue((type, condition));
            while (_lines.Count > MaxLines)
                _lines.Dequeue();
        }
    }
}
