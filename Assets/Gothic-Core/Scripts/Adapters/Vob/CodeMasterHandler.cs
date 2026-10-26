using System;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// Handles zCCodeMaster: a combination/sequence puzzle. Once all its slaves (other VOBs, e.g. switches) triggered
    /// it - in the right order if Ordered - it fires its Target. A wrong order can fire FailureTarget.
    /// </summary>
    public class CodeMasterHandler : MonoBehaviour
    {
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameStateService _gameStateService;

        private ICodeMaster _codeMaster;
        // Slaves is a native marshal which allocates a new list on every access. Read once.
        private List<string> _slaves;
        private HashSet<string> _distinctSlaves;
        private readonly HashSet<string> _triggeredSlaves = new(StringComparer.OrdinalIgnoreCase);
        private int _nextExpectedIndex;

        // DeveloperConfig.EnableEngineCodeMaster: OpenGothic CodeMaster - a key per slave and a press counter.
        private bool[] _keys;
        private int _count;

        // DeveloperConfig.EnableLogicVobSaveState: the state came from the save - no guessing from the slaves.
        private bool _isRestoredFromSave;

        private void Awake() => this.Inject();

        public void Init(ICodeMaster codeMaster)
        {
            _codeMaster = codeMaster;
            _slaves = codeMaster.Slaves;
            _distinctSlaves = new HashSet<string>(_slaves, StringComparer.OrdinalIgnoreCase);

            if (_configService == null)
                this.Inject();
            if (_configService.Dev.EnableLogicVobSaveState && LogicVobSaveState.TryTake(codeMaster, out var saved) &&
                saved.CodeMasterKeys != null)
            {
                _keys = saved.CodeMasterKeys;
                _count = saved.CodeMasterCount;
                _isRestoredFromSave = true;
                Logger.Log($"[LogicVobSave] '{codeMaster.Name}' restored from save - {_count} press(es)", LogCat.Vob);
            }
        }

        /// <summary>
        /// Saves from before code masters were saved: a slave whose trigger is used up (restored activation count) was
        /// already counted - G2 Irdorath's big bridge needs both targets, one was shot before the save and can't fire
        /// again, the bridge never moved. Partial progress is filled in. All slaves used up is
        /// unclear: solved, or saved halfway with the code master forgotten (the bridge: both targets shot, never moved).
        /// Then the slaves can fire again - a solved code isn't touched again by the player anyway.
        /// </summary>
        private void Start()
        {
            if (_isRestoredFromSave || _codeMaster == null || !_configService.Dev.EnableLogicVobSaveState ||
                !_configService.Dev.EnableEngineCodeMaster)
                return;

            var usedUp = new bool[_slaves.Count];
            var usedUpCount = 0;
            for (var i = 0; i < _slaves.Count; i++)
            {
                if (GetSlaveActivationCount(_slaves[i]) <= 0)
                    continue;
                usedUp[i] = true;
                usedUpCount++;
            }

            if (usedUpCount == 0)
                return;

            if (usedUpCount == _slaves.Count)
            {
                foreach (var slave in _slaves)
                    ResetSlaveActivationCount(slave);
                Logger.Log($"[CodeMaster] '{_codeMaster.Name}': all {_slaves.Count} slave(s) used up in a save without " +
                           "code master state - they can fire again", LogCat.Vob);
                return;
            }

            _keys = usedUp;
            _count = usedUpCount;
            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': {usedUpCount}/{_slaves.Count} slave(s) already fired " +
                       "before the save - counted (save without code master state)", LogCat.Vob);
        }

        private void ResetSlaveActivationCount(string slaveName)
        {
            var key = slaveName.ToUpper();
            if (_gameStateService.VobsTrigger.TryGetValue(key, out var zone) && zone?.Go != null && zone.Go)
                zone.Go.GetComponentInChildren<TriggerZoneHandler>(true)?.ResetActivationCount();
            if (_gameStateService.VobsTriggerScript.TryGetValue(key, out var script) && script?.Go != null && script.Go)
                script.Go.GetComponentInChildren<TriggerScriptHandler>(true)?.ResetActivationCount();
            if (_gameStateService.VobsTriggerList.TryGetValue(key, out var list) && list?.Go != null && list.Go)
                list.Go.GetComponentInChildren<TriggerListHandler>(true)?.ResetActivationCount();
        }

        private int GetSlaveActivationCount(string slaveName)
        {
            var key = slaveName.ToUpper();
            if (_gameStateService.VobsTrigger.TryGetValue(key, out var zone) && zone?.Go != null && zone.Go)
                return zone.Go.GetComponentInChildren<TriggerZoneHandler>(true)?.ActivationCount ?? 0;
            if (_gameStateService.VobsTriggerScript.TryGetValue(key, out var script) && script?.Go != null && script.Go)
                return script.Go.GetComponentInChildren<TriggerScriptHandler>(true)?.ActivationCount ?? 0;
            if (_gameStateService.VobsTriggerList.TryGetValue(key, out var list) && list?.Go != null && list.Go)
                return list.Go.GetComponentInChildren<TriggerListHandler>(true)?.ActivationCount ?? 0;
            return 0;
        }

        /// <summary>
        /// DeveloperConfig.EnableLogicVobSaveState: partial progress of a code (engine mode) goes into the save.
        /// </summary>
        public bool TryCaptureState(out LogicVobSaveEntry entry)
        {
            entry = null;
            if (_codeMaster == null || _keys == null || _count == 0)
                return false;

            entry = new LogicVobSaveEntry
            {
                Key = LogicVobSaveState.MakeKey(_codeMaster),
                CodeMasterKeys = (bool[])_keys.Clone(),
                CodeMasterCount = _count
            };
            return true;
        }

        public void ReceiveTrigger(string senderName)
        {
            if (!_distinctSlaves.Contains(senderName))
            {
                Logger.LogWarning($"[CodeMaster] '{_codeMaster.Name}': trigger from unknown slave '{senderName}'",
                    LogCat.Vob);
                return;
            }

            if (_configService.Dev.EnableEngineCodeMaster)
            {
                ReceiveTriggerLikeEngine(senderName);
                return;
            }

            if (_codeMaster.Ordered)
            {
                // Index based (not name lookup), as a slave name can appear multiple times in the sequence.
                if (_nextExpectedIndex >= _slaves.Count ||
                    !string.Equals(_slaves[_nextExpectedIndex], senderName, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log($"[CodeMaster] '{_codeMaster.Name}': wrong order - got '{senderName}', " +
                               $"expected [{_nextExpectedIndex}]", LogCat.Vob);
                    if (_codeMaster.FirstFalseIsFailure)
                        FireFailure();
                    Reset();
                    return;
                }
                _nextExpectedIndex++;
            }
            else
            {
                _triggeredSlaves.Add(senderName);
            }

            var triggeredCount = _codeMaster.Ordered ? _nextExpectedIndex : _triggeredSlaves.Count;
            var requiredCount = _codeMaster.Ordered ? _slaves.Count : _distinctSlaves.Count;
            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': slave '{senderName}' OK " +
                       $"({triggeredCount}/{requiredCount})", LogCat.Vob);

            if (triggeredCount >= requiredCount)
            {
                Logger.Log($"[CodeMaster] '{_codeMaster.Name}': all slaves triggered -> '{_codeMaster.Target}'",
                    LogCat.Vob);
                Reset();
                _vobService.DispatchTrigger(_codeMaster.Target, _codeMaster.Name);
            }
        }

        /// <summary>
        /// The engine counts every press. Ordered: a slave only counts as set when it comes at its place; after as many
        /// presses as slaves the code is judged - all set: target, else failureTarget (G2 Irdorath's touchplate rooms:
        /// a wrong order spawns monsters and starts over). firstFalseIsFailure fails at the first wrong press.
        /// Before, a wrong press reset at once and the failure target never fired.
        /// </summary>
        private void ReceiveTriggerLikeEngine(string senderName)
        {
            var slaveCount = _slaves.Count;
            if (_keys == null || _keys.Length != slaveCount)
                _keys = new bool[slaveCount];

            // Ordered: the press counts at its own place (a slave name can appear more than once in the sequence).
            int slaveIndex;
            if (_codeMaster.Ordered && _count < slaveCount &&
                string.Equals(_slaves[_count], senderName, StringComparison.OrdinalIgnoreCase))
                slaveIndex = _count;
            else
                slaveIndex = _slaves.FindIndex(s => string.Equals(s, senderName, StringComparison.OrdinalIgnoreCase));

            if (!_codeMaster.Ordered || _count == slaveIndex)
            {
                _keys[slaveIndex] = true;
            }
            else if (_codeMaster.FirstFalseIsFailure)
            {
                Logger.Log($"[CodeMaster] '{_codeMaster.Name}': '{senderName}' at press {_count + 1}, slave " +
                           $"[{slaveIndex}] - first false is failure", LogCat.Vob);
                ResetEngineState();
                FireFailure();
                return;
            }

            _count++;
            var setCount = 0;
            foreach (var key in _keys)
                if (key) setCount++;
            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': '{senderName}' (slave [{slaveIndex}]) press {_count}/" +
                       $"{slaveCount}, {setCount} set", LogCat.Vob);
            if (_count < slaveCount)
                return;

            if (setCount == slaveCount)
            {
                Logger.Log($"[CodeMaster] '{_codeMaster.Name}': all slaves triggered -> '{_codeMaster.Target}'",
                    LogCat.Vob);
                ResetEngineState();
                if (!string.IsNullOrEmpty(_codeMaster.Target))
                    _vobService.DispatchTrigger(_codeMaster.Target, _codeMaster.Name);
            }
            else if (_codeMaster.Ordered)
            {
                Logger.Log($"[CodeMaster] '{_codeMaster.Name}': wrong order - failure", LogCat.Vob);
                ResetEngineState();
                FireFailure();
            }
        }

        private void ResetEngineState()
        {
            if (_keys != null)
                Array.Clear(_keys, 0, _keys.Length);
            _count = 0;
        }

        public void ReceiveUntrigger(string senderName)
        {
            if (!_codeMaster.UntriggeredCancels)
                return;

            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': untrigger from '{senderName}' - resetting sequence",
                LogCat.Vob);
            Reset();
            ResetEngineState();
        }

        private void FireFailure()
        {
            var failureTarget = _codeMaster.FailureTarget;
            if (string.IsNullOrEmpty(failureTarget))
                return;

            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': firing FailureTarget '{failureTarget}'", LogCat.Vob);
            _vobService.DispatchTrigger(failureTarget, _codeMaster.Name);
        }

        private void Reset()
        {
            _triggeredSlaves.Clear();
            _nextExpectedIndex = 0;
        }
    }
}
