using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Vm;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob
{
    public class TriggerScriptHandler : MonoBehaviour
    {
        [Inject] private readonly VmService _vmService;

        private ITriggerScript _triggerScript;

        public void Init(ITriggerScript triggerScript)
        {
            _triggerScript = triggerScript;
        }

        /// <summary>
        /// Called programmatically when a mob-grab or Wld_SendTrigger activates this trigger.
        /// </summary>
        public void Trigger()
        {
            // Wld_SendTrigger can arrive from a world's INIT_/startup Daedalus scripts (e.g. G2
            // Renovation's INIT_NEWWORLD on a fresh new game) while this VOB's GameObject is still
            // parented under the disabled vobRoot — Awake/DI injection hasn't run yet. Inject on
            // demand instead of NRE-ing the whole world-load pipeline.
            if (_vmService == null)
                gameObject.Inject();

            if (!_triggerScript.IsEnabled || !_triggerScript.TryConsumeActivation())
                return;

            var funcName = _triggerScript.Function;
            Logger.Log($"[TriggerScript] calling '{funcName}' ({_triggerScript.CountCanBeActivated} of " +
                       $"{_triggerScript.MaxActivationCount} activations left)", LogCat.Vob);
            var sym = _vmService.Vm.GetSymbolByName(funcName);
            if (sym == null)
            {
                Logger.LogWarning($"[TriggerScript] function '{funcName}' not found in Daedalus VM", LogCat.Vob);
                return;
            }
            _vmService.Vm.Call(sym.Index);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(Constants.PlayerTag))
                return;

            if (!_triggerScript.ReactToOnTouch)
            {
                Logger.LogWarning($"oCTriggerScript {_triggerScript.Function}: not ReactToOnTouch - other trigger " +
                                  "types not implemented", LogCat.Vob);
                return;
            }

            Trigger();
        }
    }
}
