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
    /// Handles zCMessageFilter: intercepts OnTrigger/OnUntrigger and converts them
    /// to a (possibly different) action on its named Target.
    /// E.g. receives OnTrigger → forwards as OnTrigger, OnUntrigger, Enable, Disable, or Toggle.
    /// </summary>
    public class MessageFilterHandler : MonoBehaviour
    {
        [Inject] private VobService _vobService;

        private IMessageFilter _vob;

        private void Awake() => this.Inject();

        public void Init(IMessageFilter vob)
        {
            _vob = vob;
            Logger.Log($"[MessageFilter] '{vob.Name}' init — target='{vob.Target}' OnTrigger={vob.OnTrigger} OnUntrigger={vob.OnUntrigger}", LogCat.Vob);
        }

        public void Trigger()
        {
            Logger.Log($"[MessageFilter] '{_vob.Name}' Trigger() → action={_vob.OnTrigger} target='{_vob.Target}'", LogCat.Vob);
            ExecuteAction(_vob.OnTrigger);
        }

        public void Untrigger()
        {
            Logger.Log($"[MessageFilter] '{_vob.Name}' Untrigger() → action={_vob.OnUntrigger} target='{_vob.Target}'", LogCat.Vob);
            ExecuteAction(_vob.OnUntrigger);
        }

        private void ExecuteAction(MessageFilterAction action)
        {
            if (action == MessageFilterAction.None || string.IsNullOrEmpty(_vob.Target))
                return;

            switch (action)
            {
                case MessageFilterAction.Trigger:
                    _vobService.DispatchTrigger(_vob.Target);
                    break;
                case MessageFilterAction.Untrigger:
                    _vobService.DispatchUntrigger(_vob.Target);
                    break;
                case MessageFilterAction.Enable:
                case MessageFilterAction.Disable:
                case MessageFilterAction.Toggle:
                    Logger.LogWarning($"[MessageFilter] '{_vob.Name}' action={action} not yet implemented (target='{_vob.Target}')", LogCat.Vob);
                    break;
            }
        }
    }
}
