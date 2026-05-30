#if GUZ_HVR_INSTALLED
using GUZ.Core.Adapters.Vob;
using GUZ.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.VR.Adapters.Vob.Container
{
    /// <summary>
    /// Handle Socket-events for a whole Container (e.g., chest) and its corresponding sockets (rings where we put items into).
    /// </summary>
    public class VRVobContainerSocketInventory : MonoBehaviour
    {
        [Inject] private VRWeaponService _vrWeaponService;
        
        public void OnBeforeGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            Debug.Log("Undraw");
            _vrWeaponService.PlayUndrawSound(grabbable.GetComponentInParent<VobLoader>().Container);
        }

        public void OnReleased(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            Debug.Log("Draw");
            _vrWeaponService.PlayDrawSound(grabbable.GetComponentInParent<VobLoader>().Container);
        }
    }
}
#endif
