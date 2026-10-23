using System.Collections;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Links an interactable to its home slot (the backpack to the shoulder): it starts in the slot, and whenever
    /// a hand lets go of it, it returns to the slot. Sits next to the interactable's VrInteractable.
    /// The slot should have this interactable as its linked interactable (VrSocketSlot.LinkedInteractable).
    /// </summary>
    public class VrSocketLink : MonoBehaviour
    {
        [Tooltip("The slot the interactable belongs to.")]
        [SerializeField] private VrSocketSlot _slot;

        private VrInteractable _interactable;
        private Coroutine _returnRoutine;

        public VrSocketSlot Slot => _slot;

        private void Awake()
        {
            _interactable = GetComponent<VrInteractable>();
            if (_interactable == null)
            {
                Logger.LogError($"[Socket] {name} needs a {nameof(VrInteractable)} next to {nameof(VrSocketLink)}.",
                    LogCat.VR);
                return;
            }

            _interactable.Released += OnReleased;
        }

        private void Start()
        {
            ReturnToSlot(true);
        }

        private void OnDestroy()
        {
            if (_interactable != null)
                _interactable.Released -= OnReleased;
        }

        private void OnReleased(VrGrabArgs args)
        {
            if (!args.IsHandOrDistance || !isActiveAndEnabled)
                return;

            // Not inside the backend's release callback: the backend may still be busy with the release.
            if (_returnRoutine != null)
                StopCoroutine(_returnRoutine);
            _returnRoutine = StartCoroutine(ReturnNextFrame());
        }

        private IEnumerator ReturnNextFrame()
        {
            yield return null;
            _returnRoutine = null;
            ReturnToSlot(false);
        }

        private void ReturnToSlot(bool silent)
        {
            if (_slot == null || _interactable == null)
                return;

            // Still in the other hand, or a slot detected it already.
            if (_interactable.IsHeld || _slot.Held == _interactable)
                return;

            if (!_slot.TryPlace(_interactable, silent))
                Logger.LogWarning($"[Socket] {_interactable.name} couldn't return to {_slot.name}.", LogCat.VR);
        }
    }
}
