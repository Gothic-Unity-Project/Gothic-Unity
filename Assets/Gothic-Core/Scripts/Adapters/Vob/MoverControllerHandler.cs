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
    /// Handles zCMoverController: sends specific keyframe commands to a named mover.
    /// Activated via DispatchTrigger so it can be part of any trigger chain.
    /// </summary>
    public class MoverControllerHandler : MonoBehaviour
    {
        [Inject] private VobService _vobService;

        private IMoverController _vob;

        private void Awake() => this.Inject();

        public void Init(IMoverController vob) => _vob = vob;

        public void Trigger()
        {
            if (!_vobService.TryGetMovers(_vob.Target, out var containers))
            {
                Logger.LogWarning($"[MoverController] '{_vob.Name}': target mover '{_vob.Target}' not found", LogCat.Vob);
                return;
            }

            Logger.Log($"[MoverController] '{_vob.Name}' → '{_vob.Target}' msg={_vob.Message} key={_vob.Key}", LogCat.Vob);

            foreach (var container in containers)
            {
                if (container?.Go == null) continue;
                var adapter = container.Go.GetComponentInChildren<MoverAdapter>();
                if (adapter == null) continue;

                switch (_vob.Message)
                {
                    case MoverMessageType.Next:
                        adapter.GoToKeyNext();
                        break;
                    case MoverMessageType.Previous:
                        adapter.GoToKeyPrev();
                        break;
                    case MoverMessageType.FixedDirect:
                        adapter.GoToKeyFixed(_vob.Key, direct: true);
                        break;
                    case MoverMessageType.FixedOrder:
                        adapter.GoToKeyFixed(_vob.Key, direct: false);
                        break;
                }
            }
        }
    }
}
