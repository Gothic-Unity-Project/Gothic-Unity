using Gothic.Core.Adapters.Vob;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The trigger of the hand holding a torch lights it or puts it out (TorchService) - Gothic's use of a torch.
    /// </summary>
    public class VRTorchLighter : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly IVrHaptics _haptics;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly TorchService _torchService;

        private void Update()
        {
            TryToggle(VrHandSide.Left, _vrPlayerService.GrabbedItemLeft);
            TryToggle(VrHandSide.Right, _vrPlayerService.GrabbedItemRight);
        }

        private void TryToggle(VrHandSide side, GameObject heldItem)
        {
            if (heldItem == null || !_vrInput.GetButton(side, VrButton.Trigger).IsJustActivated)
                return;

            var container = heldItem.GetComponentInParent<VobLoader>()?.Container;
            if (container == null || !_torchService.Toggle(container))
                return;

            _haptics.Vibrate(side, VrHapticPreset.Info);
        }
    }
}
