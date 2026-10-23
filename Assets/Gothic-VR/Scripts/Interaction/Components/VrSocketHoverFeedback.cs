using Gothic.VR.Interaction.Models;
using UnityEngine;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Ring feedback of a VrSocketSlot: while an item hovers the slot, the ring grows and shows the valid (slot is
    /// free) or invalid (slot is occupied) material. Otherwise, it shows the normal material at its original scale,
    /// or nothing (Hide While Idle, e.g. holsters at the hip).
    /// </summary>
    public class VrSocketHoverFeedback : MonoBehaviour
    {
        [Tooltip("The slot to watch. Empty: the VrSocketSlot on this GameObject or its parents.")]
        [SerializeField] private VrSocketSlot _slot;

        [Tooltip("Renderer of the ring.")]
        [SerializeField] private Renderer _ringRenderer;

        [Tooltip("Transform scaled while hovered. Empty: the ring renderer's transform.")]
        [SerializeField] private Transform _scaleTarget;

        [Tooltip("Scale factor while an item hovers the slot.")]
        [SerializeField] private float _hoverScale = 1.25f;

        [Tooltip("Material while nothing hovers the slot.")]
        [SerializeField] private Material _normalMaterial;

        [Tooltip("Material while an item hovers a free slot.")]
        [SerializeField] private Material _validMaterial;

        [Tooltip("Material while an item hovers an occupied slot.")]
        [SerializeField] private Material _invalidMaterial;

        [Tooltip("Shows the ring only while an item hovers the slot.")]
        [SerializeField] private bool _isHiddenWhileIdle;

        private Vector3 _originalScale;
        private bool _isSubscribed;

        private Transform ScaleTarget => _scaleTarget != null
            ? _scaleTarget
            : _ringRenderer != null ? _ringRenderer.transform : null;

        private void Awake()
        {
            if (_slot == null)
                _slot = GetComponentInParent<VrSocketSlot>(true);
            if (ScaleTarget != null)
                _originalScale = ScaleTarget.localScale;
            if (_normalMaterial == null && _ringRenderer != null)
                _normalMaterial = _ringRenderer.sharedMaterial;

            if (_slot == null)
                return;

            _slot.HoverEntered += OnHoverChanged;
            _slot.HoverExited += OnHoverChanged;
            _slot.Grabbed += OnHeldChanged;
            _slot.Released += OnHeldChanged;
            _isSubscribed = true;
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnDestroy()
        {
            if (!_isSubscribed || _slot == null)
                return;

            _slot.HoverEntered -= OnHoverChanged;
            _slot.HoverExited -= OnHoverChanged;
            _slot.Grabbed -= OnHeldChanged;
            _slot.Released -= OnHeldChanged;
        }

        private void OnHoverChanged(VrInteractable interactable)
        {
            Refresh();
        }

        private void OnHeldChanged(VrGrabArgs args)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (_slot == null)
                return;

            var isHovered = _slot.IsHovered;
            var material = !isHovered ? _normalMaterial : _slot.IsOccupied ? _invalidMaterial : _validMaterial;
            if (_ringRenderer != null && material != null)
                _ringRenderer.sharedMaterial = material;
            if (_ringRenderer != null && _isHiddenWhileIdle)
                _ringRenderer.enabled = isHovered;

            var scaleTarget = ScaleTarget;
            if (scaleTarget != null)
                scaleTarget.localScale = isHovered ? _originalScale * _hoverScale : _originalScale;
        }
    }
}
