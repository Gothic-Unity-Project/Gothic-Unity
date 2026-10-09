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
    public class CodeMasterHandler : MonoBehaviour
    {
        [Inject] private VobService _vobService;

        private ICodeMaster _codeMaster;
        private readonly HashSet<string> _triggeredSlaves = new();
        private int _nextExpectedIndex;

        private void Awake() => this.Inject();

        public void Init(ICodeMaster codeMaster)
        {
            _codeMaster = codeMaster;
        }

        public void ReceiveTrigger(string senderName)
        {
            var slaves = _codeMaster.Slaves;
            var slaveIndex = slaves.FindIndex(s => string.Equals(s, senderName, System.StringComparison.OrdinalIgnoreCase));

            if (slaveIndex < 0)
            {
                Logger.LogWarning($"[CodeMaster] '{_codeMaster.Name}': trigger from unknown slave '{senderName}'", LogCat.Vob);
                return;
            }

            if (_codeMaster.Ordered)
            {
                if (slaveIndex != _nextExpectedIndex)
                {
                    Logger.Log($"[CodeMaster] '{_codeMaster.Name}': wrong order — got slave[{slaveIndex}] '{senderName}', expected [{_nextExpectedIndex}]", LogCat.Vob);
                    if (_codeMaster.FirstFalseIsFailure)
                        FireFailure();
                    Reset();
                    return;
                }
                _nextExpectedIndex++;
            }

            _triggeredSlaves.Add(senderName.ToUpper());
            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': slave '{senderName}' OK ({_triggeredSlaves.Count}/{slaves.Count})", LogCat.Vob);

            if (_triggeredSlaves.Count >= slaves.Count)
            {
                Logger.Log($"[CodeMaster] '{_codeMaster.Name}': all slaves triggered → '{_codeMaster.Target}'", LogCat.Vob);
                Reset();
                _vobService.DispatchTrigger(_codeMaster.Target);
            }
        }

        public void ReceiveUntrigger(string senderName)
        {
            if (!_codeMaster.UntriggeredCancels)
                return;

            Logger.Log($"[CodeMaster] '{_codeMaster.Name}': untrigger from '{senderName}' — resetting sequence", LogCat.Vob);
            Reset();
        }

        private void FireFailure()
        {
            var ft = _codeMaster.FailureTarget;
            if (!string.IsNullOrEmpty(ft))
            {
                Logger.Log($"[CodeMaster] '{_codeMaster.Name}': firing FailureTarget '{ft}'", LogCat.Vob);
                _vobService.DispatchTrigger(ft);
            }
        }

        private void Reset()
        {
            _triggeredSlaves.Clear();
            _nextExpectedIndex = 0;
        }
    }
}
