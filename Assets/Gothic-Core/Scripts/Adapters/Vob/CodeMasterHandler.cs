using System;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// Handles zCCodeMaster: a combination/sequence puzzle, like the engine (OpenGothic CodeMaster::onTrigger). Every
    /// press of a slave (another VOB, e.g. a switch or touch plate) counts. Once there were as many presses as slaves, the
    /// code is judged: all slaves pressed - in their order if Ordered - fires Target, a wrong order fires FailureTarget.
    /// FirstFalseIsFailure fails at the first wrong press instead.
    /// </summary>
    public class CodeMasterHandler : MonoBehaviour
    {
        [Inject] private readonly VobService _vobService;

        private ICodeMaster _codeMaster;
        // Slaves is a native marshal which allocates a new list on every access. Read once.
        private List<string> _slaves;
        private HashSet<string> _distinctSlaves;
        // Per slave position: pressed (in its turn, if ordered). And the presses since the code was last judged.
        private bool[] _pressedSlots;
        private int _pressCount;

        private void Awake() => this.Inject();

        public void Init(ICodeMaster codeMaster)
        {
            _codeMaster = codeMaster;
            _slaves = codeMaster.Slaves;
            _distinctSlaves = new HashSet<string>(_slaves, StringComparer.OrdinalIgnoreCase);
            _pressedSlots = new bool[_slaves.Count];
        }

        public void ReceiveTrigger(string senderName)
        {
            if (!_distinctSlaves.Contains(senderName))
            {
                Logger.LogWarning($"[CodeMaster] '{_codeMaster.Name}': trigger from unknown slave '{senderName}'",
                    LogCat.Vob);
                return;
            }

            var slot = FindSlot(senderName);
            if (slot >= 0)
            {
                _pressedSlots[slot] = true;
            }
            else if (_codeMaster.FirstFalseIsFailure)
            {
                Logger.Log($"[CodeMaster] '{_codeMaster.Name}': wrong press '{senderName}' at {_pressCount}",
                    LogCat.Vob);
                Fail();
                return;
            }

            _pressCount++;
            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': '{senderName}' pressed ({_pressCount}/{_slaves.Count}, " +
                       $"{(slot >= 0 ? "right" : "wrong")})", LogCat.Vob);
            if (_pressCount < _slaves.Count)
                return;

            if (Array.TrueForAll(_pressedSlots, isPressed => isPressed))
            {
                Logger.Log($"[CodeMaster] '{_codeMaster.Name}': solved -> '{_codeMaster.Target}'", LogCat.Vob);
                Reset();
                _vobService.DispatchTrigger(_codeMaster.Target, _codeMaster.Name);
            }
            else if (_codeMaster.Ordered)
            {
                Fail();
            }
        }

        public void ReceiveUntrigger(string senderName)
        {
            if (!_codeMaster.UntriggeredCancels)
                return;

            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': untrigger from '{senderName}' - resetting sequence",
                LogCat.Vob);
            Reset();
        }

        /// <summary>
        /// The position the press fills: ordered, only the one whose turn it is (a name can appear several times in the
        /// sequence); unordered, the first one of that slave not pressed yet, else one already pressed - it still counts
        /// as a press. -1 for a press out of order.
        /// </summary>
        private int FindSlot(string senderName)
        {
            if (_codeMaster.Ordered)
            {
                return _pressCount < _slaves.Count &&
                       string.Equals(_slaves[_pressCount], senderName, StringComparison.OrdinalIgnoreCase)
                    ? _pressCount
                    : -1;
            }

            var pressedSlot = -1;
            for (var i = 0; i < _slaves.Count; i++)
            {
                if (!string.Equals(_slaves[i], senderName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!_pressedSlots[i])
                    return i;
                pressedSlot = i;
            }

            return pressedSlot;
        }

        /// <summary>
        /// Like the engine, a failure starts the code over and fires FailureTarget.
        /// </summary>
        private void Fail()
        {
            Reset();
            var failureTarget = _codeMaster.FailureTarget;
            if (string.IsNullOrEmpty(failureTarget))
                return;

            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': failed -> '{failureTarget}'", LogCat.Vob);
            _vobService.DispatchTrigger(failureTarget, _codeMaster.Name);
        }

        private void Reset()
        {
            Array.Clear(_pressedSlots, 0, _pressedSlots.Length);
            _pressCount = 0;
        }
    }
}
