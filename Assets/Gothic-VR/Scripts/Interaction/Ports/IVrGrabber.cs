using System;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// Something that grabs interactables: a hand, a distance grabber or a socket (see IVrSocket).
    /// Implemented by a backend bridge next to the backend's grabber.
    /// </summary>
    public interface IVrGrabber
    {
        VrGrabberKind Kind { get; }

        /// <summary>
        /// Only meaningful for Kind Hand and Distance.
        /// </summary>
        VrHandSide HandSide { get; }

        /// <summary>
        /// The currently held interactable or null.
        /// </summary>
        VrInteractable Held { get; }

        /// <summary>
        /// Raised before the backend moves the interactable (e.g. to turn its physics on).
        /// </summary>
        event Action<VrGrabArgs> BeforeGrabbed;

        event Action<VrGrabArgs> Grabbed;
        event Action<VrGrabArgs> Released;

        void ForceRelease();
    }
}
