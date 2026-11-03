using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// A larger grab area of an interactable while it's in a slot (e.g. the backpack on the shoulder: out of sight
    /// and scaled down there). The BoxCollider is on VobItemSocketed: nothing collides with it, only the hands' near
    /// grab finds it. Off while a hand holds the interactable, so that the other hand reaches past it into the
    /// backpack. Its size is kept in world space, whatever the slot scaled the interactable to.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class VrSocketedGrabArea : MonoBehaviour
    {
        [Tooltip("Edge length of the grab area in meters (world space) while the interactable is in a slot.")]
        [SerializeField] private float _size = 0.3f;

        private BoxCollider _collider;
        private VrInteractable _interactable;

        private void Awake()
        {
            _collider = GetComponent<BoxCollider>();
            // XRI ignores triggers for grabbing.
            _collider.isTrigger = false;
            _collider.enabled = false;

            _interactable = GetComponentInParent<VrInteractable>(true);
            if (_interactable == null)
            {
                Logger.LogWarning($"[Socket] {name} has no {nameof(VrInteractable)} above it. No grab area.",
                    LogCat.VR);
                return;
            }

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
            var isSocketed = args.Kind == VrGrabberKind.Socket;
            // The slot scaled the interactable already (VrSocketSlot fits before the grab events).
            if (isSocketed)
                FitToWorldSize();

            _collider.enabled = isSocketed;
        }

        private void OnReleased(VrGrabArgs args)
        {
            if (args.Kind == VrGrabberKind.Socket)
                _collider.enabled = false;
        }

        private void FitToWorldSize()
        {
            var scale = transform.lossyScale;
            _collider.center = Vector3.zero;
            _collider.size = new Vector3(_size / scale.x, _size / scale.y, _size / scale.z);
        }
    }
}
