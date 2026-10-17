using System.Collections;
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
    /// Handles zCTriggerList: fires its targets (each with its own delay) all at once, one after the other per
    /// activation (Next) or a random one.
    /// </summary>
    public class TriggerListHandler : MonoBehaviour
    {
        [Inject] private readonly VobService _vobService;

        private ITriggerList _triggerList;
        private int _activationCount;

        private void Awake() => this.Inject();

        public void Init(ITriggerList triggerList)
        {
            _triggerList = triggerList;
        }

        public void Trigger()
        {
            if (!_triggerList.IsEnabled)
                return;

            // MaxActivationCount: -1 = unlimited, 0 = never, N = fire N times.
            var max = _triggerList.MaxActivationCount;
            if (max == 0)
                return;
            if (max > 0 && _activationCount >= max)
                return;

            _activationCount++;

            var targets = _triggerList.Targets;
            if (targets.Count == 0)
            {
                Logger.LogWarning($"[TriggerList] '{_triggerList.Name}' has no targets", LogCat.Vob);
                return;
            }

            Logger.Log($"[TriggerList] '{_triggerList.Name}' triggered (mode={_triggerList.Mode}, " +
                       $"activation={_activationCount})", LogCat.Vob);

            switch (_triggerList.Mode)
            {
                case TriggerBatchMode.All:
                    foreach (var target in targets)
                        ScheduleDispatch(target.Name, (float)target.Delay.TotalSeconds);
                    break;
                case TriggerBatchMode.Next:
                    var index = _triggerList.ActTarget % targets.Count;
                    ScheduleDispatch(targets[index].Name, (float)targets[index].Delay.TotalSeconds);
                    _triggerList.ActTarget = (byte)((index + 1) % targets.Count);
                    break;
                case TriggerBatchMode.Random:
                    var randomIndex = Random.Range(0, targets.Count);
                    ScheduleDispatch(targets[randomIndex].Name, (float)targets[randomIndex].Delay.TotalSeconds);
                    break;
            }
        }

        private void ScheduleDispatch(string name, float delay)
        {
            if (delay <= 0f)
                _vobService.DispatchTrigger(name, _triggerList.Name);
            else
                StartCoroutine(DelayedDispatch(name, delay));
        }

        private IEnumerator DelayedDispatch(string name, float delay)
        {
            yield return new WaitForSeconds(delay);
            _vobService.DispatchTrigger(name, _triggerList.Name);
        }
    }
}
