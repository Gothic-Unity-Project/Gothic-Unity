using Gothic.Core.Adapters.Vob;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The trigger of the hand holding a torch lights it or puts it out (TorchService) - Gothic's use of a torch. It
    /// also tells TorchService what the hands hold, which decides the torch that lights the world as the hero light.
    /// </summary>
    public class VRTorchLighter : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly IVrHaptics _haptics;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly TorchService _torchService;

        private GameObject _heldLeft;
        private GameObject _heldRight;

        private void Update()
        {
            var left = _vrPlayerService.GrabbedItemLeft;
            var right = _vrPlayerService.GrabbedItemRight;
            if (!ReferenceEquals(left, _heldLeft) || !ReferenceEquals(right, _heldRight))
            {
                _heldLeft = left;
                _heldRight = right;
                _torchService.SetHeldTorches(GetContainer(left), GetContainer(right));
            }

            TryToggle(VrHandSide.Left, left);
            TryToggle(VrHandSide.Right, right);
        }

        private void TryToggle(VrHandSide side, GameObject heldItem)
        {
            if (heldItem == null || !_vrInput.GetButton(side, VrButton.Trigger).IsJustActivated)
                return;

            var container = GetContainer(heldItem);
            if (container == null || !_torchService.Toggle(container))
                return;

            _haptics.Vibrate(side, VrHapticPreset.Info);
        }

        private static VobContainer GetContainer(GameObject heldItem)
        {
            return heldItem != null ? heldItem.GetComponentInParent<VobLoader>()?.Container : null;
        }
    }
}
