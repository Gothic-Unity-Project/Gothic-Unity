using Gothic.Core.Const;
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
    /// Handles zCTrigger: fires Target when the player enters the bounding-box zone.
    /// Also handles programmatic OnTrigger events from other VOBs via ReceiveTrigger().
    /// </summary>
    public class TriggerZoneHandler : MonoBehaviour
    {
        [Inject] private readonly VobService _vobService;

        private ITrigger _trigger;

        private void Awake() => this.Inject();

        public void Init(ITrigger trigger) => _trigger = trigger;

        public void ReceiveTrigger()
        {
            if (!_trigger.ReactToOnTrigger)
            {
                Logger.LogWarning($"[TriggerZone] '{_trigger.Name}': received trigger but ReactToOnTrigger=false",
                    LogCat.Vob);
                return;
            }
            FireTarget();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_trigger.ReactToOnTouch || !RespondsTo(other))
                return;

            Logger.Log($"[TriggerZone] '{_trigger.Name}' OnTriggerEnter player", LogCat.Vob);
            FireTarget();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!RespondsTo(other) || !_trigger.SendUntrigger || string.IsNullOrEmpty(_trigger.Target))
                return;

            Logger.Log($"[TriggerZone] '{_trigger.Name}' OnTriggerExit -> DispatchUntrigger '{_trigger.Target}'",
                LogCat.Vob);
            _vobService.DispatchUntrigger(_trigger.Target, _trigger.Name);
        }

        private bool RespondsTo(Collider other)
        {
            return _trigger.RespondToPC && other.CompareTag(Constants.PlayerTag);
        }

        private void FireTarget()
        {
            if (!_trigger.IsEnabled || string.IsNullOrEmpty(_trigger.Target) || !_trigger.TryConsumeActivation())
                return;

            var target = _trigger.Target;
            Logger.Log($"[TriggerZone] '{_trigger.Name}' -> '{target}' ({_trigger.CountCanBeActivated} of " +
                       $"{_trigger.MaxActivationCount} activations left)", LogCat.Vob);
            _vobService.DispatchTrigger(target, _trigger.Name);
        }
    }
}
