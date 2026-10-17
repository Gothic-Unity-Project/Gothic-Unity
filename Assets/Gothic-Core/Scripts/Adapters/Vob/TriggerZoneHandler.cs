using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.World;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// Handles zCTrigger: fires Target when the player enters the bounding-box zone.
    /// Also handles programmatic OnTrigger events from other VOBs via ReceiveTrigger().
    /// </summary>
    public class TriggerZoneHandler : MonoBehaviour, IDamageable
    {
        [Inject] private VobService _vobService;

        private ITrigger _trigger;
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
            entry = _activationCount > 0 && _trigger != null
                ? new LogicVobSaveEntry
                {
                    Key = LogicVobSaveState.MakeKey(_trigger), ActivationCount = _activationCount
                }
                : null;
            return entry != null;
        }

        private void Awake() => this.Inject();

        public void Init(ITrigger trigger)
        {
            _trigger = trigger;
            if (ReflexProjectInstaller.DIContainer.Resolve<ConfigService>().Dev.EnableLogicVobSaveState &&
                LogicVobSaveState.TryTake(_trigger, out var saved))
            {
                _activationCount = saved.ActivationCount;
                Logger.Log($"[LogicVobSave] '{_trigger.Name}' restored from save - fired {_activationCount} time(s)", LogCat.Vob);
            }
            if (_trigger.ReactToOnDamage)
                DamageTargets.Register(transform);
        }

        private void OnDestroy() => DamageTargets.Unregister(transform);

        public void ReceiveTrigger()
        {
            if (!_trigger.ReactToOnTrigger)
            {
                Logger.LogWarning($"[TriggerZone] '{_trigger.Name}': received trigger but ReactToOnTrigger=false", LogCat.Vob);
                return;
            }
            FireTarget();
        }

        /// <summary>
        /// Hit by an arrow/bolt/spell. The projectile itself is an object (RespondToObject), the shooter PC or NPC.
        /// DamageThreshold isn't checked - the projectile doesn't know its damage.
        /// </summary>
        public void OnDamage(NpcContainer attacker)
        {
            if (!_trigger.ReactToOnDamage)
                return;

            var isPlayer = attacker?.Vob != null && attacker.Vob.Player;
            var isResponding = _trigger.RespondToObject || (isPlayer ? _trigger.RespondToPC : _trigger.RespondToNPC);
            if (!isResponding)
            {
                Logger.Log($"[TriggerZone] '{_trigger.Name}' hit, but doesn't respond to this attacker", LogCat.Vob);
                return;
            }

            Logger.Log($"[TriggerZone] '{_trigger.Name}' hit by a projectile (DamageThreshold={_trigger.DamageThreshold})",
                LogCat.Vob);
            FireTarget();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_trigger.ReactToOnTouch) return;
            if (!RespondsTo(other)) return;
            Logger.Log($"[TriggerZone] '{_trigger.Name}' OnTriggerEnter player", LogCat.Vob);
            FireTarget();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!RespondsTo(other)) return;
            Logger.Log($"[TriggerZone] '{_trigger.Name}' OnTriggerExit (SendUntrigger={_trigger.SendUntrigger})", LogCat.Vob);
            if (!_trigger.SendUntrigger) return;
            Logger.Log($"[TriggerZone] '{_trigger.Name}' OnTriggerExit → DispatchUntrigger '{_trigger.Target}'", LogCat.Vob);
            _vobService.DispatchUntrigger(_trigger.Target);
        }

        private bool RespondsTo(Collider other)
        {
            return _trigger.RespondToPC && other.CompareTag(Constants.PlayerTag);
        }

        private void FireTarget()
        {
            if (!_trigger.IsEnabled) return;

            var max = _trigger.MaxActivationCount;
            if (max == 0) return;
            if (max > 0 && _activationCount >= max) return;

            _activationCount++;
            var target = _trigger.Target;
            Logger.Log($"[TriggerZone] '{_trigger.Name}' → '{target}' (act {_activationCount}/{(max < 0 ? "∞" : max.ToString())})", LogCat.Vob);
            // The trigger itself is the sender - a CodeMaster only counts its slaves by name (G2 Irdorath's big
            // bridge: both targets hit, "trigger from unknown slave ''", the bridge stayed).
            _vobService.DispatchTrigger(target, _trigger.Name);
        }
    }
}
