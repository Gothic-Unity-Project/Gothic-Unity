using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// Handles zCTriggerWorldStart: fires Target once when the world finishes loading.
    /// Like OpenGothic (WorldObjects::triggerOnStart):
    /// - A world restored from a save game or from a visit earlier in this session fires nothing. Its state contains
    ///   the result already.
    /// - FireOnce triggers fire only when the world is entered for the first time (G1 save games don't store
    ///   HasFired).
    /// </summary>
    public class TriggerWorldStartHandler : MonoBehaviour
    {
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly SaveGameService _saveGameService;

        private ITriggerWorldStart _vob;

        private void Awake() => this.Inject();

        public void Init(ITriggerWorldStart vob) => _vob = vob;

        private void Start()
        {
            if (_saveGameService.IsWorldRestored)
                return;
            if (_vob.FireOnce && (_vob.HasFired || !_saveGameService.IsWorldEnteredFirstTime))
                return;

            var target = _vob.Target;
            if (string.IsNullOrEmpty(target))
                return;

            Logger.Log($"[TriggerWorldStart] '{_vob.Name}' -> '{target}' (FireOnce={_vob.FireOnce})", LogCat.Vob);
            _vob.HasFired = true;
            _vobService.DispatchTrigger(target, _vob.Name);
        }
    }
}
