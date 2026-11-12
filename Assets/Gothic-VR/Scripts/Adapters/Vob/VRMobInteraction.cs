using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob
{
    /// <summary>
    /// Uses a mob (wheel, lever, shrine, bed, lidless container, ...) when the hero grabs it - the single entry point
    /// for a VR grab on a mob. Sits next to the mob's VrInteractable. Two hands toggle once (until released).
    /// The logic itself is platform neutral (MobInteractionService).
    /// </summary>
    public class VRMobInteraction : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly MobInteractionService _mobInteractionService;

        private bool _mobActivated;
        private VrInteractable _interactable;

        /// <summary>
        /// Subscribed in Start (not Awake), so that e.g. VRSeat handles a grab first (as before).
        /// </summary>
        private void Start()
        {
            if (!TryGetComponent(out _interactable))
                return;

            _interactable.Grabbed += OnGrabbed;
            _interactable.Released += OnReleased;
        }

        private void OnDestroy()
        {
            if (_interactable == null)
                return;

            _interactable.Grabbed -= OnGrabbed;
            _interactable.Released -= OnReleased;
        }

        private void OnGrabbed(VrGrabArgs args)
        {
            if (_mobActivated)
                return;

            var vobLoader = GetComponentInParent<VobLoader>();
            var vob = vobLoader?.Container?.Vob;
            // A lidless container (mummy) is a plain mob here too: the grab toggles its loot (VRPlayerService).
            if (!_mobInteractionService.ShouldHandleGrab(vob) && !vob.IsLidlessContainer())
                return;

            _mobActivated = true;
            Logger.Log($"[VRMobInteraction.OnGrabbed] mob={vobLoader.gameObject.name}", LogCat.Vob);
            _vrPlayerService.HandleMobGrab(vobLoader);
        }

        private void OnReleased(VrGrabArgs _)
        {
            _mobActivated = false;
        }

        // Culled or disabled while held: no Released comes - the next grab must still work.
        private void OnDisable()
        {
            _mobActivated = false;
        }
    }
}
