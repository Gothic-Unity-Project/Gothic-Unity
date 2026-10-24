#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Translates HVR's (grabber, grabbable) event parameters into our VrGrabArgs.
    /// </summary>
    public static class HvrGrabArgsFactory
    {
        public static VrGrabArgs Create(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            return new VrGrabArgs(
                grabber.ToVrKind(),
                grabber.ToVrHandSide(),
                GetInteractable(grabbable),
                grabber != null ? grabber.gameObject : null);
        }

        /// <summary>
        /// Our prefabs have a VrInteractable next to every HVRGrabbable. Grabbables from elsewhere (e.g. HVR's own
        /// prefabs) get one on first use, so game code never receives null.
        /// </summary>
        public static VrInteractable GetInteractable(HVRGrabbable grabbable)
        {
            if (grabbable == null)
                return null;

            var interactable = grabbable.GetComponent<VrInteractable>();
            if (interactable != null)
                return interactable;

            Logger.LogWarning($"[HVR] {grabbable.name} has no VrInteractable. Adding it at runtime.", LogCat.VR);
            interactable = grabbable.gameObject.AddComponent<VrInteractable>();
            grabbable.gameObject.AddComponent<HvrInteractableBridge>();
            return interactable;
        }
    }
}
#endif
