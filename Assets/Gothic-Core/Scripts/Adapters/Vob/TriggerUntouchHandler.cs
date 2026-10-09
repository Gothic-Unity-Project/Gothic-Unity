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
    /// Handles zCTriggerUntouch: fires Target when the player EXITS the bounding-box zone.
    /// </summary>
    public class TriggerUntouchHandler : MonoBehaviour
    {
        [Inject] private VobService _vobService;

        private string _target;

        private void Awake() => this.Inject();

        public void Init(ITriggerUntouch vob) => _target = vob.Target;

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(Constants.PlayerTag)) return;
            if (string.IsNullOrEmpty(_target)) return;
            Logger.Log($"[TriggerUntouch] OnTriggerExit → '{_target}'", LogCat.Vob);
            _vobService.DispatchTrigger(_target);
        }
    }
}
