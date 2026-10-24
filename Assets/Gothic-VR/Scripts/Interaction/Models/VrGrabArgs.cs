using Gothic.VR.Interaction.Components;
using UnityEngine;

namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// Payload of all grab, release and hover events (VrInteractable, IVrGrabber, IVrSocket).
    /// </summary>
    public readonly struct VrGrabArgs
    {
        /// <summary>
        /// What grabbed (or released) the interactable.
        /// </summary>
        public readonly VrGrabberKind Kind;

        /// <summary>
        /// The hand of the grabber. Only meaningful for Kind Hand and Distance; Left otherwise.
        /// </summary>
        public readonly VrHandSide HandSide;

        /// <summary>
        /// The object that is grabbed, released or hovered.
        /// </summary>
        public readonly VrInteractable Interactable;

        /// <summary>
        /// GameObject of the grabber (hand, socket, ...). Use GetComponent&lt;IVrSocket&gt;() for socket details.
        /// </summary>
        public readonly GameObject GrabberGo;

        public VrGrabArgs(VrGrabberKind kind, VrHandSide handSide, VrInteractable interactable, GameObject grabberGo)
        {
            Kind = kind;
            HandSide = handSide;
            Interactable = interactable;
            GrabberGo = grabberGo;
        }

        public bool IsHandOrDistance => Kind is VrGrabberKind.Hand or VrGrabberKind.Distance;
    }
}
