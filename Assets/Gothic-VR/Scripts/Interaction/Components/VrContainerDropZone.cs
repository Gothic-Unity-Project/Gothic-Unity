using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Trigger volume (e.g. inside an open chest). An item released by a hand while it's inside the volume is put
    /// into a free slot of the container. Needs a trigger collider on this GameObject on a layer that collides with
    /// held items.
    /// The placement happens one frame after the release, so that the backend has finished the release (and a slot
    /// that caught the item itself wins).
    /// </summary>
    public class VrContainerDropZone : MonoBehaviour
    {
        [Tooltip("GameObject with the IVrSocketContainer (e.g. VrSocketContainer). Empty: this GameObject or its " +
                 "parents.")]
        [SerializeField] private GameObject _containerGo;

        private readonly Dictionary<VrInteractable, int> _colliderCounts = new();
        private readonly List<VrInteractable> _pendingDrops = new();
        private readonly List<VrInteractable> _dropBuffer = new();
        private IVrSocketContainer _container;

        public IVrSocketContainer Container
        {
            get
            {
                if (_container == null)
                    _container = _containerGo != null
                        ? _containerGo.GetComponent<IVrSocketContainer>()
                        : GetComponentInParent<IVrSocketContainer>(true);
                return _container;
            }
        }

        private void Awake()
        {
            if (Container == null)
                Logger.LogWarning($"[Socket] Drop zone {name} has no IVrSocketContainer.", LogCat.VR);
        }

        private void OnDisable()
        {
            foreach (var interactable in _colliderCounts.Keys)
            {
                if (interactable != null)
                    interactable.Released -= OnReleased;
            }

            _colliderCounts.Clear();
            _pendingDrops.Clear();
        }

        private void Update()
        {
            if (_pendingDrops.Count == 0)
                return;

            _dropBuffer.AddRange(_pendingDrops);
            _pendingDrops.Clear();

            foreach (var interactable in _dropBuffer)
            {
                // Destroyed, grabbed again (other hand) or already caught by a slot.
                if (interactable == null || interactable.IsHeld || Container == null)
                    continue;

                if (!Container.TryPlace(interactable))
                    Logger.Log($"[Socket] No free slot for {interactable.name} in {name}.", LogCat.VR);
            }

            _dropBuffer.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            var interactable = GetInteractable(other);
            if (interactable == null)
                return;

            _colliderCounts.TryGetValue(interactable, out var count);
            _colliderCounts[interactable] = count + 1;
            if (count == 0)
                interactable.Released += OnReleased;
        }

        private void OnTriggerExit(Collider other)
        {
            var interactable = GetInteractable(other);
            if (interactable == null || !_colliderCounts.TryGetValue(interactable, out var count))
                return;

            if (count > 1)
            {
                _colliderCounts[interactable] = count - 1;
                return;
            }

            _colliderCounts.Remove(interactable);
            interactable.Released -= OnReleased;
        }

        private void OnReleased(VrGrabArgs args)
        {
            if (!args.IsHandOrDistance || !VrSocketSlot.IsItem(args.Interactable))
                return;

            if (!_pendingDrops.Contains(args.Interactable))
                _pendingDrops.Add(args.Interactable);
        }

        private static VrInteractable GetInteractable(Collider other)
        {
            var body = other.attachedRigidbody;
            var interactable = body != null ? body.GetComponent<VrInteractable>() : null;
            return interactable != null ? interactable : other.GetComponentInParent<VrInteractable>();
        }
    }
}
