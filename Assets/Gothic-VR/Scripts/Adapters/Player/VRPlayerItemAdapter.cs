#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Manager;
using Gothic.Core.Services.World;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Sockets;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    public class VRPlayerItemAdapter : MonoBehaviour
    {
        [Inject] private readonly MarvinService _marvinService;
        [Inject] private readonly SaveGameService _saveGameService;


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

            // Promote item to the world VOB list if it isn't already there (e.g. grabbed from a chest
            // socket — chest items are created as fresh VOBs not in CurrentWorldData.Vobs).
            // For regular world items already in the list this is a no-op add + position track.
            var vobLoader = grabbable.GetComponentInParent<VobLoader>();
            if (vobLoader?.Container != null)
                _saveGameService.PromoteChestItemToWorld(vobLoader.Container);
        }

    }
}
#endif
