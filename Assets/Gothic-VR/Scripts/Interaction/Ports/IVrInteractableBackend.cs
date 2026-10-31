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

        /// <summary>
        /// false: no hand or socket can hover or grab it (e.g. a spell's invisible rune riding on the hand).
        /// </summary>
        bool IsGrabEnabled { get; set; }

        /// <summary>
        /// false: only a hand right at it grabs it, no distance grab (the favorites ring's items).
        /// </summary>
        bool IsDistanceGrabbable { get; set; }

        /// <summary>
        /// true: while one hand holds it, the other hand can't grab it (a throwable spell's rune: the other hand
        /// throws).
        /// </summary>
        bool IsOneHandOnly { get; set; }
        void ForceRelease();
        /// <param name="isChildLinked">Colliders on child GameObjects grab this interactable too (HVR maps a collider
        /// only on its own GameObject; XRI maps every collected collider anyway).</param>
        void SetupColliders(bool isChildLinked = false);
        void SetSocketSounds(AudioClip socketed, AudioClip unsocketed);
    }
}
