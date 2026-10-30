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
    /// Handles zCTriggerList like Gothic: a trigger sends its targets the trigger after their delays - all of them
    /// one after the other (All: the delays add up), the next one per activation (Next) or a random one (Random).
    ///
    /// It runs on save game fields of the VOB (zCTriggerList, zCVob), a loaded list continues: ActTarget is the target
    /// to send next, NextOnTimer the seconds until then (0 = nothing pending; written when the world is saved or
    /// left). SendOnTrigger is the direction (triggers, not untriggers) and always true (VobInitializerDomain).
    /// </summary>
    public class TriggerListHandler : MonoBehaviour
    {
        [Inject] private readonly VobService _vobService;

        private ITriggerList _triggerList;
        private bool _isSending;

        // Time.time when ActTarget gets the trigger. Only valid while _isSending.
        private float _sendTime;

        private void Awake()
        {
            this.Inject();
            GlobalEventDispatcher.StoreWorldStateStart.AddListener(StoreTimer);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.StoreWorldStateStart.RemoveListener(StoreTimer);
        }

        public void Init(ITriggerList triggerList)
        {
            _triggerList = triggerList;

            // A list which was sending when the world was saved/left continues with the remaining delay.
            _isSending = _triggerList.NextOnTimer > 0f && _triggerList.Targets.Count > 0;
            _sendTime = Time.time + _triggerList.NextOnTimer;
            enabled = _isSending;
        }

        public void Trigger()
        {
            if (!_triggerList.IsEnabled || !_triggerList.TryConsumeActivation())
                return;

            var targets = _triggerList.Targets;
            if (targets.Count == 0)
            {
                Logger.LogWarning($"[TriggerList] '{_triggerList.Name}' has no targets", LogCat.Vob);
                return;
            }

            Logger.Log($"[TriggerList] '{_triggerList.Name}' triggered (mode={_triggerList.Mode}, " +
                       $"{_triggerList.CountCanBeActivated} of {_triggerList.MaxActivationCount} activations left)",
                LogCat.Vob);

            _triggerList.ActTarget = _triggerList.Mode switch
            {
                TriggerBatchMode.All => (byte)0,
                TriggerBatchMode.Random => (byte)Random.Range(0, targets.Count),
                _ => (byte)(_triggerList.ActTarget % targets.Count)
            };
            ScheduleActTarget();
        }

        private void Update()
        {
            if (Time.time >= _sendTime)
                SendActTarget();
        }

        private void ScheduleActTarget()
        {
            _isSending = true;
            _sendTime = Time.time + (float)_triggerList.Targets[_triggerList.ActTarget % _triggerList.Targets.Count]
                .Delay.TotalSeconds;
            enabled = true;

            // Without a delay, it's sent right away (same frame).
            if (Time.time >= _sendTime)
                SendActTarget();
        }

        private void SendActTarget()
        {
            var targets = _triggerList.Targets;
            var index = _triggerList.ActTarget % targets.Count;
            _isSending = false;
            enabled = false;

            // Set before sending: the target can trigger this list again.
            var isAllSent = false;
            if (_triggerList.Mode == TriggerBatchMode.All)
            {
                isAllSent = index + 1 >= targets.Count;
                _triggerList.ActTarget = (byte)(isAllSent ? 0 : index + 1);
            }
            else if (_triggerList.Mode == TriggerBatchMode.Next)
            {
                _triggerList.ActTarget = (byte)((index + 1) % targets.Count);
            }

            _vobService.DispatchTrigger(targets[index].Name, _triggerList.Name);

            if (_triggerList.Mode == TriggerBatchMode.All && !isAllSent && !_isSending)
                ScheduleActTarget();
        }

        private void StoreTimer(bool isSaving)
        {
            if (_triggerList == null)
                return;

            // A pending target due right now still needs to count as pending.
            _triggerList.NextOnTimer = _isSending ? Mathf.Max(_sendTime - Time.time, 0.001f) : 0f;
        }
    }
}
