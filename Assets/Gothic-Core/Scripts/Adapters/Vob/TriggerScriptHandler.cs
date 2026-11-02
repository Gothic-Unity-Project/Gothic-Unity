using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.World;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Vm;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob
{
    public class TriggerScriptHandler : MonoBehaviour, IDamageable
    {
        [Inject] private readonly VmService _vmService;

        private ITriggerScript _triggerScript;
        private int _activationCount;
        public int ActivationCount => _activationCount;

        /// <summary>
        /// Can fire again - a code master without saved state frees its used-up slaves (CodeMasterHandler.Start).
        /// </summary>
        public void ResetActivationCount() => _activationCount = 0;

        /// <summary>
        /// DeveloperConfig.EnableLogicVobSaveState: fired since the world was loaded - maxActivationCount holds after
        /// loading a save.
        /// </summary>
        public bool TryCaptureState(out LogicVobSaveEntry entry)
        {
            entry = _activationCount > 0 && _triggerScript != null
                ? new LogicVobSaveEntry
                {
                    Key = LogicVobSaveState.MakeKey(_triggerScript), ActivationCount = _activationCount
                }
                : null;
            return entry != null;
        }

        private void OnDestroy() => DamageTargets.Unregister(transform);

        public void Init(ITriggerScript triggerScript)
        {
            _triggerScript = triggerScript;
            if (ReflexProjectInstaller.DIContainer.Resolve<ConfigService>().Dev.EnableLogicVobSaveState &&
                LogicVobSaveState.TryTake(_triggerScript, out var saved))
            {
                _activationCount = saved.ActivationCount;
                Logger.Log($"[LogicVobSave] '{_triggerScript.Name}' restored from save - fired {_activationCount} time(s)", LogCat.Vob);
            }
            if (_triggerScript.ReactToOnDamage)
                DamageTargets.Register(transform);
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
            Logger.Log($"[TriggerScript] calling '{funcName}' - activation {_activationCount}/{max}", LogCat.Vob);
            var sym = _vmService.Vm.GetSymbolByName(funcName);
            if (sym == null)
            {
                Logger.LogWarning($"[TriggerScript] function '{funcName}' not found in Daedalus VM", LogCat.Vob);
                return;
            }
            _vmService.Vm.Call(sym.Index);
        }

        /// <summary>
        /// Hit by an arrow/bolt/spell (reactToOnDamage).
        /// </summary>
        public void OnDamage(NpcContainer attacker)
        {
            if (!_triggerScript.ReactToOnDamage)
                return;

            Logger.Log($"[TriggerScript] '{_triggerScript.Function}' hit by a projectile", LogCat.Vob);
            Trigger();
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
