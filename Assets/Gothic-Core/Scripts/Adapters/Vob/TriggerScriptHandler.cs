using Gothic.Core.Const;
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
        
        private TriggerScript _triggerScript;
        
        
        public void Init(ITriggerScript triggerScript)
        {
            _triggerScript = (TriggerScript)triggerScript;
        }
        
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(Constants.PlayerTag))
            {
                return;
            }

            if (!_triggerScript.ReactToOnTouch)
            {
                Logger.LogWarning($"oCTriggerScript {other.gameObject.name} is triggering {_triggerScript.Function} but not for ReactOnTrigger." +
                                  $"But any other trigger types aren't implemented yet.", LogCat.Vob);
                return;
            }
            
            // If -1, then it can be triggered infinite. Only decrease if >0
            if (_triggerScript.CountCanBeActivated > 0)
                _triggerScript.CountCanBeActivated--;

            if (_triggerScript.CountCanBeActivated == 0)
                return;

            _vmService.Vm.Call(_triggerScript.Function);
        }
    }
}
