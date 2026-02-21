using GUZ.Core.Const;
using GUZ.Core.Services.Vm;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;

namespace GUZ.Core.Adapters.Vob
{
    public class TriggerScriptHandler : MonoBehaviour
    {
        [Inject] private VmService _vmService;
        
        private ITriggerScript _triggerScript;
        
        
        public void Init(ITriggerScript triggerScript)
        {
            _triggerScript = triggerScript;
        }
        
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(Constants.PlayerTag))
            {
                return;
            }
            
            _vmService.Vm.Call(_triggerScript.Function);
        }
    }
}
