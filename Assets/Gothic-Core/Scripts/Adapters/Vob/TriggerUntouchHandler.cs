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
        [Inject] private readonly VobService _vobService;

        private ITriggerUntouch _vob;

        private void Awake() => this.Inject();

        public void Init(ITriggerUntouch vob) => _vob = vob;

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(Constants.PlayerTag) || string.IsNullOrEmpty(_vob.Target))
                return;

            Logger.Log($"[TriggerUntouch] '{_vob.Name}' OnTriggerExit -> '{_vob.Target}'", LogCat.Vob);
            _vobService.DispatchTrigger(_vob.Target, _vob.Name);
        }
    }
}
