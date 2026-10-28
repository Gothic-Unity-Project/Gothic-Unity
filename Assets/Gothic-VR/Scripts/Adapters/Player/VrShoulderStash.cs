using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Over-the-shoulder stash, like the HVR backpack's socket container: an item let go at the shoulder while the
    /// backpack sits there goes into the inventory (VRBackpack.TryStash), the backpack stays on. A hand carrying such
    /// an item into the spot is announced like reaching the slot (VrBodySlot).
    /// Sits on the backpack's shoulder slot (the VrSocketSlot linked to the backpack).
    /// </summary>
    [RequireComponent(typeof(VrBodySlot))]
    public class VrShoulderStash : MonoBehaviour
    {
        [Tooltip("The spot a hand lets go of an item in. Empty: the collider on this GameObject.")] [SerializeField]
        private Collider _zone;

        private readonly bool[] _isHandInZone = new bool[2];
        private readonly List<VrInteractable> _pendingStashes = new();
        private readonly List<IVrGrabber> _hands = new();

        private VrSocketSlot _slot;
        private VrBodySlot _bodySlot;
        private IVrRig _rig;
        private VRBackpack _backpack;

        private void Start()
        {
            _slot = GetComponent<VrSocketSlot>();
            _bodySlot = GetComponent<VrBodySlot>();
            if (_zone == null)
                _zone = GetComponent<Collider>();

            _rig = GetComponentInParent<IVrRig>();
            _backpack = _slot.LinkedInteractable != null ? _slot.LinkedInteractable.GetComponent<VRBackpack>() : null;
            if (_zone == null || _rig == null || _backpack == null)
            {
                Logger.LogWarning($"[Backpack] {name}: {nameof(VrShoulderStash)} needs a zone collider, an IVrRig " +
                                  "above it and a slot linked to the backpack. Items can't be stashed over the " +
                                  "shoulder.", LogCat.VR);
                enabled = false;
                return;
            }

            foreach (var grabber in _rig.HandGrabbers)
            {
                if (grabber.Kind != VrGrabberKind.Hand)
                    continue;

                grabber.Released += OnHandReleased;
                _hands.Add(grabber);
            }
        }

        private void OnDestroy()
        {
            foreach (var hand in _hands)
                hand.Released -= OnHandReleased;
        }

        private void Update()
        {
            // Stashed here, not within the backend's release event.
            foreach (var item in _pendingStashes)
            {
                if (item != null && _backpack.TryStash(item))
                    _slot.PlayPlacedSound(item);
            }

            _pendingStashes.Clear();

            foreach (var hand in _hands)
            {
                var index = (int)hand.HandSide;
                var isInZone = CanStash(hand.Held) && IsInZone(hand.HandSide);
                if (isInZone && !_isHandInZone[index])
                    _bodySlot.AnnounceReached(hand.HandSide);

                _isHandInZone[index] = isInZone;
            }
        }

        private void OnHandReleased(VrGrabArgs args)
        {
            if (isActiveAndEnabled && CanStash(args.Interactable) && IsInZone(args.HandSide))
                _pendingStashes.Add(args.Interactable);
        }

        /// <summary>
        /// The backpack sits on the shoulder and takes the item.
        /// </summary>
        private bool CanStash(VrInteractable item)
        {
            return item != null && _slot.Held == _slot.LinkedInteractable && _backpack.CanStash(item);
        }

        private bool IsInZone(VrHandSide side)
        {
            var hand = (side == VrHandSide.Left ? _rig.LeftHand : _rig.RightHand).position;
            return (_zone.ClosestPoint(hand) - hand).sqrMagnitude < 0.0001f;
        }
    }
}
