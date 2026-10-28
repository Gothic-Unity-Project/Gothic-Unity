using Gothic.Core;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// A VrSocketSlot worn on the body (shoulders, hip holsters). These slots are behind the back or out of sight, so
    /// a hand reaching one it can use is announced (GlobalEventDispatcher.BodySlotReached, VrHapticsService ticks):
    /// - a hand holding something the slot takes now (an item at a free holster, the backpack at its shoulder),
    /// - an empty hand at the slot's content (the backpack on the shoulder, a holstered item).
    /// A ray pointing at the content doesn't count, only a hand reaching it.
    /// </summary>
    [RequireComponent(typeof(VrSocketSlot))]
    public class VrBodySlot : MonoBehaviour
    {
        private VrSocketSlot _slot;
        private IVrRig _rig;
        private VrInteractable _content;

        private void Awake()
        {
            _slot = GetComponent<VrSocketSlot>();
            _rig = GetComponentInParent<IVrRig>();
        }

        private void OnEnable()
        {
            _slot.HoverEntered += OnSlotHoverEntered;
            _slot.Grabbed += OnPlaced;
            _slot.Released += OnRemoved;
            Watch(_slot.Held);
        }

        private void OnDisable()
        {
            _slot.HoverEntered -= OnSlotHoverEntered;
            _slot.Grabbed -= OnPlaced;
            _slot.Released -= OnRemoved;
            Watch(null);
        }

        /// <summary>
        /// The hand reached the slot with something it can use (also for uses beyond the slot, e.g. VrShoulderStash).
        /// </summary>
        public void AnnounceReached(VrHandSide side)
        {
            GlobalEventDispatcher.BodySlotReached.Invoke((int)side);
        }

        private void OnSlotHoverEntered(VrInteractable interactable)
        {
            if (_slot.IsValidFor(interactable) && TryGetHand(interactable, out var side))
                AnnounceReached(side);
        }

        private void OnPlaced(VrGrabArgs args)
        {
            Watch(args.Interactable);
        }

        private void OnRemoved(VrGrabArgs args)
        {
            Watch(null);
        }

        private void Watch(VrInteractable content)
        {
            if (_content != null)
                _content.HoverEntered -= OnContentHoverEntered;

            _content = content;
            if (_content != null)
                _content.HoverEntered += OnContentHoverEntered;
        }

        private void OnContentHoverEntered(VrGrabArgs args)
        {
            if (args.Kind == VrGrabberKind.Hand && TryGetHand(null, out var side, args.HandSide))
                AnnounceReached(side);
        }

        /// <summary>
        /// The hand holding the interactable (null: the empty hand on handSide).
        /// </summary>
        private bool TryGetHand(VrInteractable held, out VrHandSide side, VrHandSide? handSide = null)
        {
            side = default;
            if (_rig == null)
                return false;

            foreach (var grabber in _rig.HandGrabbers)
            {
                if (grabber.Kind != VrGrabberKind.Hand || grabber.Held != held ||
                    (handSide.HasValue && grabber.HandSide != handSide.Value))
                    continue;

                side = grabber.HandSide;
                return true;
            }

            return false;
        }
    }
}
