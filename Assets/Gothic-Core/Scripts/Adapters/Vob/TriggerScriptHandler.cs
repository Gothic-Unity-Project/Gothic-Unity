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
        [Inject] private VmService _vmService;

        private ITriggerScript _triggerScript;
        private int _activationCount;

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

            if (!_triggerScript.IsEnabled)
                return;

            // MaxActivationCount: -1 = unlimited, 0 = never, N = fire N times.
            // CountCanBeActivated is a save-game field (starts at 0 on fresh load) — do NOT use as the limit.
            var max = _triggerScript.MaxActivationCount;
            if (max == 0)
                return;
            if (max > 0 && _activationCount >= max)
                return;

            _activationCount++;
            var funcName = _triggerScript.Function;
            Logger.Log($"[TriggerScript] calling '{funcName}' — activation {_activationCount}/{(max < 0 ? "∞" : max.ToString())}", LogCat.Vob);
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
                Logger.LogWarning($"oCTriggerScript {_triggerScript.Function}: not ReactToOnTouch — other trigger types not implemented", LogCat.Vob);
                return;
            }

            Trigger();
        }
    }
}
