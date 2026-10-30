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

        // Takes items beyond the slot itself (e.g. the shoulder stash). A hand holding one of these counts as
        // holding something the slot takes.
        private IVrDropTarget _extraTarget;

        private void Awake()
        {
            _slot = GetComponent<VrSocketSlot>();
            _extraTarget = GetComponent<IVrDropTarget>();
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
        /// A held item reached the slot (hover, or a drop zone contact): announces the holding hand if the slot or
        /// an IVrDropTarget on it takes the item. The one place that decides "usable by this grabber".
        /// </summary>
        public void OnReached(VrInteractable held)
        {
            if (held != null && (_slot.IsValidFor(held) || (_extraTarget != null && _extraTarget.CanTake(held))) &&
                TryGetHand(held, out var side))
                GlobalEventDispatcher.BodySlotReached.Invoke((int)side);
        }

        private void OnSlotHoverEntered(VrInteractable interactable)
        {
            OnReached(interactable);
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
                GlobalEventDispatcher.BodySlotReached.Invoke((int)side);
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
