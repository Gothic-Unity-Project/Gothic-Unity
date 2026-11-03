using System;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Interaction
{
    /// <summary>
    /// Trigger volume of a drop zone (inside an open chest, at the shoulder). Needs a trigger collider on this
    /// GameObject on a layer that collides with held items, and an IVrDropTarget on this GameObject or a parent.
    /// Only reports contacts to VrDropZoneService, which hands an item let go inside to the target a frame later.
    /// </summary>
    public class VrDropZone : MonoBehaviour
    {
        [Inject] private readonly VrDropZoneService _dropZoneService;

        private IVrDropTarget _target;

        /// <summary>
        /// A held item entered the zone (first contact of the item). Check IVrDropTarget.CanTake before reacting.
        /// </summary>
        public event Action<VrInteractable> HeldItemEntered;

        private IVrDropTarget Target
        {
            get
            {
                if (_target == null)
                    _target = GetComponentInParent<IVrDropTarget>(true);
                return _target;
            }
        }

        private void OnDisable()
        {
            if (_target != null)
                _dropZoneService?.Unregister(_target);
        }

        private void OnTriggerEnter(Collider other)
        {
            var interactable = GetInteractable(other);
            if (interactable == null || Target == null)
                return;

            if (_dropZoneService.ItemEntered(Target, interactable) && interactable.IsHeld)
                HeldItemEntered?.Invoke(interactable);
        }

        private void OnTriggerExit(Collider other)
        {
            var interactable = GetInteractable(other);
            if (interactable != null && Target != null)
                _dropZoneService.ItemExited(Target, interactable);
        }

        private static VrInteractable GetInteractable(Collider other)
        {
            var body = other.attachedRigidbody;
            var interactable = body != null ? body.GetComponent<VrInteractable>() : null;
            return interactable != null ? interactable : other.GetComponentInParent<VrInteractable>();
        }
    }
}
