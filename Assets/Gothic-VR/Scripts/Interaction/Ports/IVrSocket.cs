using Gothic.VR.Interaction.Components;
using UnityEngine;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// A slot that holds one interactable: hip holster, shoulder, backpack slot, chest slot, NPC loot slot.
    /// Grabbed/Released (IVrGrabber) are raised when an interactable is placed into or taken out of it.
    /// </summary>
    public interface IVrSocket : IVrGrabber
    {
        Transform Transform { get; }
        bool IsOccupied { get; }

        /// <summary>
        /// True for over-the-shoulder sockets (the backpack and the right shoulder holster).
        /// </summary>
        bool IsShoulder { get; }

        /// <summary>
        /// Places the interactable into this socket, ignoring the socket's filters.
        /// </summary>
        /// <param name="silent">If true, no "placed" sound is played.</param>
        bool TryPlace(VrInteractable interactable, bool silent);

        /// <summary>
        /// Played when an interactable without own socket sounds is placed.
        /// </summary>
        AudioClip PlacedSoundFallback { get; set; }

        /// <summary>
        /// Played when an interactable without own socket sounds is removed.
        /// </summary>
        AudioClip RemovedSoundFallback { get; set; }
    }
}
