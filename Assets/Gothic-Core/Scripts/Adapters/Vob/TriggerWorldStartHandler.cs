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
    /// Handles zCTriggerWorldStart: fires Target once when the world finishes loading.
    /// If FireOnce is true and the world was already entered, HasFired prevents re-firing (save/load support).
    /// </summary>
    public class TriggerWorldStartHandler : MonoBehaviour
    {
        [Inject] private VobService _vobService;

        private ITriggerWorldStart _vob;

        private void Awake() => this.Inject();

        public void Init(ITriggerWorldStart vob) => _vob = vob;

        private void Start()
        {
            if (_vob.FireOnce && _vob.HasFired) return;

            var target = _vob.Target;
            if (string.IsNullOrEmpty(target)) return;

            Logger.Log($"[TriggerWorldStart] '{_vob.Name}' → '{target}' (FireOnce={_vob.FireOnce})", LogCat.Vob);
            _vob.HasFired = true;
            _vobService.DispatchTrigger(target);
        }
    }
}
