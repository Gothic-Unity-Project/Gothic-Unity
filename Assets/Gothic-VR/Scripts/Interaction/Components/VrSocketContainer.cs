using System.Collections.Generic;
using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Backend-neutral group of VrSocketSlots (backpack inventory, chest content).
    /// Uses the slots listed in the inspector or, if the list is empty, all VrSocketSlots below this GameObject.
    /// </summary>
    public class VrSocketContainer : MonoBehaviour, IVrSocketContainer
    {
        [Tooltip("Slots of this container in placement order. Empty: all VrSocketSlots below this GameObject.")]
        [SerializeField] private List<VrSocketSlot> _slots = new();

        private readonly List<IVrSocket> _sockets = new();
        private bool _isCollected;
        private bool _isAutoCollected;
        private bool _isSilent;

        public IReadOnlyList<IVrSocket> Sockets
        {
            get
            {
                CollectSlots();
                return _sockets;
            }
        }

        /// <summary>
        /// True if one of the slots has a backend part of the active VR backend (e.g. XriSocketBridge on XRI).
        /// </summary>
        public bool IsActive
        {
            get
            {
                foreach (var slot in Slots)
                {
                    if (slot != null && slot.HasActiveBackend)
                        return true;
                }

                return false;
            }
        }

        public bool IsSilent => _isSilent;

        public IReadOnlyList<VrSocketSlot> Slots
        {
            get
            {
                CollectSlots();
                return _slots;
            }
        }

        private void Awake()
        {
            CollectSlots();
        }

        /// <summary>
        /// Re-collects the slots below this GameObject (after slots were added or removed at runtime).
        /// Only if no slots are listed in the inspector.
        /// </summary>
        public void RefreshSlots()
        {
            _isCollected = false;
            CollectSlots();
        }

        public bool TryFindFree(VrInteractable interactable, out IVrSocket socket)
        {
            socket = null;
            if (interactable == null)
                return false;

            foreach (var slot in Slots)
            {
                if (slot == null || slot.IsOccupied || !slot.isActiveAndEnabled)
                    continue;

                socket = slot;
                return true;
            }

            return false;
        }

        public bool TryPlace(VrInteractable interactable, bool silent = false)
        {
            return TryFindFree(interactable, out var socket) && socket.TryPlace(interactable, silent);
        }

        public void SetSilent(bool isSilent)
        {
            _isSilent = isSilent;
            foreach (var slot in Slots)
            {
                if (slot != null)
                    slot.SetSilent(isSilent);
            }
        }

        private void CollectSlots()
        {
            if (_isCollected)
                return;

            _isCollected = true;
            if (_isAutoCollected || _slots.Count == 0 || _slots.TrueForAll(slot => slot == null))
            {
                _isAutoCollected = true;
                _slots.Clear();
                GetComponentsInChildren(true, _slots);
            }

            _sockets.Clear();
            foreach (var slot in _slots)
            {
                if (slot == null)
                    continue;

                _sockets.Add(slot);
                slot.SetSilent(_isSilent);
            }
        }
    }
}
