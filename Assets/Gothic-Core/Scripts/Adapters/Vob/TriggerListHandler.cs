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
    public class TriggerListHandler : MonoBehaviour
    {
        [Inject] private VobService _vobService;

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

            Logger.Log($"[TriggerList] '{_triggerList.Name}' triggered (mode={_triggerList.Mode}, activation={_activationCount})", LogCat.Vob);

            switch (_triggerList.Mode)
            {
                case TriggerBatchMode.All:
                    foreach (var t in targets)
                        ScheduleDispatch(t.Name, (float)t.Delay.TotalSeconds);
                    break;
                case TriggerBatchMode.Next:
                    var idx = _triggerList.ActTarget;
                    ScheduleDispatch(targets[idx].Name, (float)targets[idx].Delay.TotalSeconds);
                    _triggerList.ActTarget = (byte)((idx + 1) % targets.Count);
                    break;
                case TriggerBatchMode.Random:
                    var rand = Random.Range(0, targets.Count);
                    ScheduleDispatch(targets[rand].Name, (float)targets[rand].Delay.TotalSeconds);
                    break;
            }
        }

        private void ScheduleDispatch(string name, float delay)
        {
            if (delay <= 0f)
                _vobService.DispatchTrigger(name);
            else
                StartCoroutine(DelayedDispatch(name, delay));
        }

        private IEnumerator DelayedDispatch(string name, float delay)
        {
            yield return new WaitForSeconds(delay);
            _vobService.DispatchTrigger(name);
        }
    }
}
