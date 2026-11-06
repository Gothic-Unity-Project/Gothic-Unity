using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// A larger grab area of an interactable while it's in a slot (e.g. the backpack on the shoulder: out of sight
    /// and scaled down there). The BoxCollider is on VobItemSocketed: nothing collides with it, only the hands find it
    /// (near grab and line). Off while a hand holds the interactable, so that the other hand reaches past it into the
    /// backpack. Its size is kept in world space, whatever the slot scaled the interactable to.
    /// Items (oCItem.prefab, Is Sized By Slot): as large as the slot's ring, so the line aims at the ring - in the
    /// slots the line may take from (backpack, chest, loot, trade), not in the player's holsters.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class VrSocketedGrabArea : MonoBehaviour
    {
        [Tooltip("Edge length of the grab area in meters (world space) while the interactable is in a slot.")]
        [SerializeField] private float _size = 0.3f;

        [Tooltip("Sized like the slot's ring instead (items): the hand's line aims at the ring, not at the item " +
                 "scaled down inside it. Only in slots the line may take from (VrSocketSlot.IsLineGrabbable).")]
        [SerializeField] private bool _isSizedBySlot;

        // A slot's ring is this much larger than the item it fits (backpack slot: 0.07 m ring, 0.05 m item).
        private const float _ringToSlotSize = 1.4f;

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
            var size = _size;
            Vector3? center = null;
            if (isSocketed && _isSizedBySlot)
            {
                var slot = args.GrabberGo != null ? args.GrabberGo.GetComponent<VrSocketSlot>() : null;
                isSocketed = slot != null && slot.IsLineGrabbable;
                if (isSocketed)
                {
                    size = slot.Size * _ringToSlotSize;
                    // On the ring's middle - the slot centers the item's bounds there, not its pivot.
                    center = slot.AttachPoint.position;
                }
            }

            // The slot scaled the interactable already (VrSocketSlot fits before the grab events).
            if (isSocketed)
                FitToWorldSize(size, center);

            _collider.enabled = isSocketed;
        }

        private void OnReleased(VrGrabArgs args)
        {
            if (args.Kind == VrGrabberKind.Socket)
                _collider.enabled = false;
        }

        private void FitToWorldSize(float size, Vector3? worldCenter)
        {
            var scale = transform.lossyScale;
            _collider.center = worldCenter.HasValue ? transform.InverseTransformPoint(worldCenter.Value) : Vector3.zero;
            _collider.size = new Vector3(size / scale.x, size / scale.y, size / scale.z);
        }
    }
}
