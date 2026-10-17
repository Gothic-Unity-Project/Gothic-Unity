using System.Collections;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.World;
using Gothic.Core.Services.Config;
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
        [Inject] private ConfigService _configService;

        private ITriggerList _triggerList;
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
            entry = _activationCount > 0 && _triggerList != null
                ? new LogicVobSaveEntry
                {
                    Key = LogicVobSaveState.MakeKey(_triggerList), ActivationCount = _activationCount
                }
                : null;
            return entry != null;
        }

        private void Awake() => this.Inject();

        public void Init(ITriggerList triggerList)
        {
            _triggerList = triggerList;
            if (ReflexProjectInstaller.DIContainer.Resolve<ConfigService>().Dev.EnableLogicVobSaveState &&
                LogicVobSaveState.TryTake(_triggerList, out var saved))
            {
                _activationCount = saved.ActivationCount;
                Logger.Log($"[LogicVobSave] '{_triggerList.Name}' restored from save - fired {_activationCount} time(s)", LogCat.Vob);
            }
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
                    // DeveloperConfig.EnableTriggerListSummedDelays: in the engine every target's delay counts from
                    // the previous one (OpenGothic TriggerList: offset += delay) - not each from the trigger moment.
                    var offset = 0f;
                    foreach (var t in targets)
                    {
                        var delay = (float)t.Delay.TotalSeconds;
                        offset = _configService.Dev.EnableTriggerListSummedDelays ? offset + delay : delay;
                        ScheduleDispatch(t.Name, offset);
                    }
                    Logger.Log($"[TriggerList] '{_triggerList.Name}' schedule: " +
                               string.Join(", ", System.Linq.Enumerable.Select(targets, t => $"{t.Name}+{t.Delay.TotalSeconds:F1}s")),
                        LogCat.Vob);
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
