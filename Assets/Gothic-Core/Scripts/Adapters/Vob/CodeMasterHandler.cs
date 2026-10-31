using System;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services;
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
        [Inject] private readonly GameStateService _gameStateService;

        private ICodeMaster _codeMaster;
        // Slaves is a native marshal which allocates a new list on every access. Read once.
        private List<string> _slaves;
        private HashSet<string> _distinctSlaves;
        private CodeMasterState _ownState;

        private void Awake() => this.Inject();

        public void Init(ICodeMaster codeMaster)
        {
            _codeMaster = codeMaster;
            _slaves = codeMaster.Slaves;
            _distinctSlaves = new HashSet<string>(_slaves, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The progress lives with the code master's container, so a save game keeps it (CodeMastersSaveSection) - also
        /// one restored after this handler was created. Read once per press.
        /// </summary>
        private CodeMasterState GetState()
        {
            _gameStateService.VobsCodeMaster.TryGetValue(_codeMaster.Name.ToUpper(), out var container);
            var state = container != null ? container.CodeMasterState : _ownState;
            if (state == null || state.PressedSlots.Length != _slaves.Count)
            {
                if (state != null)
                    Logger.LogWarning($"[CodeMaster] '{_codeMaster.Name}': saved progress has " +
                                      $"{state.PressedSlots.Length} slots, the code master {_slaves.Count} - " +
                                      "starting over (world changed since the save?)", LogCat.Vob);

                state = new CodeMasterState(_slaves.Count);
                if (container != null)
                    container.CodeMasterState = state;
                else
                    _ownState = state;
            }

            return state;
        }

        public void ReceiveTrigger(string senderName)
        {
            if (!_distinctSlaves.Contains(senderName))
            {
                Logger.LogWarning($"[CodeMaster] '{_codeMaster.Name}': trigger from unknown slave '{senderName}'",
                    LogCat.Vob);
                return;
            }

            var state = GetState();
            var slot = FindSlot(senderName, state);
            if (slot >= 0)
            {
                state.PressedSlots[slot] = true;
            }
            else if (_codeMaster.FirstFalseIsFailure)
            {
                Logger.Log($"[CodeMaster] '{_codeMaster.Name}': wrong press '{senderName}' at {state.PressCount}",
                    LogCat.Vob);
                Fail(state);
                return;
            }

            state.PressCount++;
            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': '{senderName}' pressed " +
                       $"({state.PressCount}/{_slaves.Count}, " +
                       $"{(slot >= 0 ? "right" : "wrong")})", LogCat.Vob);
            if (state.PressCount < _slaves.Count)
                return;

            if (Array.TrueForAll(state.PressedSlots, isPressed => isPressed))
            {
                Logger.Log($"[CodeMaster] '{_codeMaster.Name}': solved -> '{_codeMaster.Target}'", LogCat.Vob);
                Reset(state);
                _vobService.DispatchTrigger(_codeMaster.Target, _codeMaster.Name);
            }
            else if (_codeMaster.Ordered)
            {
                Fail(state);
            }
        }

        public void ReceiveUntrigger(string senderName)
        {
            if (!_codeMaster.UntriggeredCancels)
                return;

            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': untrigger from '{senderName}' - resetting sequence",
                LogCat.Vob);
            Reset(GetState());
        }

        /// <summary>
        /// The position the press fills: ordered, only the one whose turn it is (a name can appear several times in the
        /// sequence); unordered, the first one of that slave not pressed yet, else one already pressed - it still counts
        /// as a press. -1 for a press out of order.
        /// </summary>
        private int FindSlot(string senderName, CodeMasterState state)
        {
            if (_codeMaster.Ordered)
            {
                return state.PressCount < _slaves.Count &&
                       string.Equals(_slaves[state.PressCount], senderName, StringComparison.OrdinalIgnoreCase)
                    ? state.PressCount
                    : -1;
            }

            var pressedSlot = -1;
            for (var i = 0; i < _slaves.Count; i++)
            {
                if (!string.Equals(_slaves[i], senderName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!state.PressedSlots[i])
                    return i;
                pressedSlot = i;
            }

            return pressedSlot;
        }

        /// <summary>
        /// Like the engine, a failure starts the code over and fires FailureTarget.
        /// </summary>
        private void Fail(CodeMasterState state)
        {
            Reset(state);
            var failureTarget = _codeMaster.FailureTarget;
            if (string.IsNullOrEmpty(failureTarget))
                return;

            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': failed -> '{failureTarget}'", LogCat.Vob);
            _vobService.DispatchTrigger(failureTarget, _codeMaster.Name);
        }

        private static void Reset(CodeMasterState state)
        {
            Array.Clear(state.PressedSlots, 0, state.PressedSlots.Length);
            state.PressCount = 0;
        }
    }
}
