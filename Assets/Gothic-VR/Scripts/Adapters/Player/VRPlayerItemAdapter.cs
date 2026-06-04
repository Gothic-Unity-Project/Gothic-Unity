#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Manager;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    public class VRPlayerItemAdapter : MonoBehaviour
    {
        [Inject] private readonly MarvinService _marvinService;


        /// <summary>
        /// We need to set isKinematic=false in BeforeGrabbed, otherwise we get a warning at OnGrabbed event time afterward.
        /// </summary>
        public void OnBeforeGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            // Ignore grabbing once, if MarvinSelectionMode is active.
            if (_marvinService.IsMarvinSelectionMode)
                return;

            // OnGrabbed is normally called multiple times. Even after an object is already socketed. If so, then let's stop Grab behaviour.
            // If we sock an object on our hips or backpack etc.
            if (grabber is HVRSocket)
                return;

            // In Gothic, Items have no physics when lying around. We need to activate physics for HVR to properly move items into our hands.
            if (grabbable.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = false;
            }
        }

    }
}
#endif
