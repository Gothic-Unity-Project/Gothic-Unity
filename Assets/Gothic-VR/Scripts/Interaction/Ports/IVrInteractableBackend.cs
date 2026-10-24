using System.Collections.Generic;
using UnityEngine;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// Implemented by the backend bridge next to a VrInteractable. Game code uses VrInteractable instead.
    /// The bridge also raises the VrInteractable events.
    /// Prefabs carry the bridges of all backends; only the bridge of the active backend is used.
    /// </summary>
    public interface IVrInteractableBackend
    {
        /// <summary>
        /// Whether this bridge belongs to the active VR backend.
        /// </summary>
        bool IsActive { get; }

        bool IsHeld { get; }

        /// <summary>
        /// True if a grab only "uses" the object (talk to an NPC, use a mob): nothing moves, nothing follows the hand
        /// and no BeforeGrabbed is raised. False if the grab holds the object like an item (HVR grabs NPCs and mobs
        /// that way, so their adapters release them again).
        /// </summary>
        bool IsSelectOnly { get; }

        IReadOnlyList<Collider> Colliders { get; }
        void ForceRelease();
        void SetupColliders();
        void SetSocketSounds(AudioClip socketed, AudioClip unsocketed);
    }
}
