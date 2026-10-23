using System;
using System.Collections.Generic;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Game-facing side of everything the player can grab or hover (items, mobs, NPCs, doors, backpack).
    /// Adapters subscribe to its C# events instead of the VR backend's events.
    /// A backend bridge (IVrInteractableBackend) on the same GameObject raises the events and does the work.
    ///
    /// Events are raised synchronously in the backend's order: HoverEntered, BeforeGrabbed, Grabbed, HoverExited,
    /// Released. BeforeGrabbed is only raised for grabbers with a bridge (player hands and sockets).
    /// </summary>
    public class VrInteractable : MonoBehaviour
    {
        public event Action<VrGrabArgs> HoverEntered;
        public event Action<VrGrabArgs> HoverExited;
        public event Action<VrGrabArgs> BeforeGrabbed;
        public event Action<VrGrabArgs> Grabbed;
        public event Action<VrGrabArgs> Released;

        private IVrInteractableBackend _backend;

        /// <summary>
        /// Held by any grabber (hand, distance grabber, socket).
        /// </summary>
        public bool IsHeld => Backend != null && Backend.IsHeld;

        /// <summary>
        /// A grab only "uses" the object (talk, use a mob) and doesn't hold it.
        /// See IVrInteractableBackend.IsSelectOnly.
        /// </summary>
        public bool IsSelectOnly => Backend != null && Backend.IsSelectOnly;

        /// <summary>
        /// Played by VrSocketSlot when this interactable is put into a slot (instead of the slot's fallback).
        /// </summary>
        public AudioClip SocketedSound { get; private set; }

        /// <summary>
        /// Played by VrSocketSlot when this interactable is taken out of a slot (instead of the slot's fallback).
        /// </summary>
        public AudioClip UnsocketedSound { get; private set; }

        public IReadOnlyList<Collider> Colliders => Backend != null ? Backend.Colliders : Array.Empty<Collider>();

        private IVrInteractableBackend Backend
        {
            get
            {
                if (_backend == null)
                    _backend = FindActiveBackend();
                return _backend;
            }
        }

        private IVrInteractableBackend FindActiveBackend()
        {
            foreach (var backend in GetComponents<IVrInteractableBackend>())
            {
                if (backend.IsActive)
                    return backend;
            }

            return null;
        }

        /// <summary>
        /// The VrInteractable on this GameObject or (if none) the first one below it.
        /// </summary>
        public static VrInteractable Find(Component component)
        {
            var interactable = component.GetComponent<VrInteractable>();
            if (interactable == null)
                interactable = component.GetComponentInChildren<VrInteractable>(true);
            return interactable;
        }

        /// <summary>
        /// Releases the interactable from all grabbers.
        /// </summary>
        public void ForceRelease()
        {
            Backend?.ForceRelease();
        }

        /// <summary>
        /// Re-collects the colliders (call after meshes/colliders were added at runtime).
        /// </summary>
        public void SetupColliders()
        {
            Backend?.SetupColliders();
        }

        /// <summary>
        /// Sounds played when this interactable is put into or taken out of a socket.
        /// </summary>
        public void SetSocketSounds(AudioClip socketed, AudioClip unsocketed)
        {
            SocketedSound = socketed;
            UnsocketedSound = unsocketed;
            Backend?.SetSocketSounds(socketed, unsocketed);
        }

        internal void RaiseHoverEntered(VrGrabArgs args)
        {
            HoverEntered?.Invoke(args);
        }

        internal void RaiseHoverExited(VrGrabArgs args)
        {
            HoverExited?.Invoke(args);
        }

        internal void RaiseBeforeGrabbed(VrGrabArgs args)
        {
            BeforeGrabbed?.Invoke(args);
        }

        internal void RaiseGrabbed(VrGrabArgs args)
        {
            Grabbed?.Invoke(args);
        }

        internal void RaiseReleased(VrGrabArgs args)
        {
            Released?.Invoke(args);
        }
    }
}
