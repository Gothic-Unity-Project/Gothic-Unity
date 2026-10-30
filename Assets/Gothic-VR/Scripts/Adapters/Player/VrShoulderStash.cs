using Gothic.Core.Logging;
using Gothic.VR.Adapters.Interaction;
using Gothic.VR.Interaction.Components;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Over-the-shoulder stash, like the HVR backpack's socket container: an item let go at the shoulder while the
    /// backpack sits there goes into the inventory (VRBackpack.TryStash), the backpack stays on. A hand carrying such
    /// an item into the spot is announced like reaching the slot (VrBodySlot).
    /// Sits on the backpack's shoulder slot (the VrSocketSlot linked to the backpack) and uses its trigger collider as
    /// the drop zone (VrDropZone reports the contacts, VrDropZoneService handles the releases).
    /// </summary>
    [RequireComponent(typeof(VrBodySlot), typeof(VrDropZone))]
    public class VrShoulderStash : MonoBehaviour, IVrDropTarget
    {
        private VrSocketSlot _slot;
        private VrBodySlot _bodySlot;
        private VrDropZone _dropZone;
        private VRBackpack _backpack;

        private void Start()
        {
            _slot = GetComponent<VrSocketSlot>();
            _bodySlot = GetComponent<VrBodySlot>();
            _dropZone = GetComponent<VrDropZone>();

            _backpack = _slot.LinkedInteractable != null ? _slot.LinkedInteractable.GetComponent<VRBackpack>() : null;
            if (GetComponent<Collider>() == null || _backpack == null)
            {
                Logger.LogWarning($"[Backpack] {name}: {nameof(VrShoulderStash)} needs a trigger collider and a " +
                                  "slot linked to the backpack. Items can't be stashed over the shoulder.", LogCat.VR);
                enabled = false;
                return;
            }

            _dropZone.HeldItemEntered += _bodySlot.OnReached;
        }

        /// <summary>
        /// The backpack sits on the shoulder and takes the item.
        /// </summary>
        public bool CanTake(VrInteractable item)
        {
            return isActiveAndEnabled && item != null && _slot.Held == _slot.LinkedInteractable &&
                   _backpack.CanStash(item);
        }

        public bool TryTake(VrInteractable item)
        {
            if (!CanTake(item) || !_backpack.TryStash(item))
                return false;

            _slot.PlayPlacedSound(item);
            return true;
        }
    }
}
