#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Vob.Container
{
    /// <summary>
    /// Handle Socket-events for a whole Container (e.g., chest) and its corresponding sockets (rings where we put items into).
    /// </summary>
    public class VRVobContainerSocketInventory : MonoBehaviour
    {
        [Inject] private VRWeaponService _vrWeaponService;

        public void OnBeforeGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            var container = grabbable.GetComponentInParent<VobLoader>()?.Container;
            if (!IsWeapon(container))
                return;
            _vrWeaponService.PlayUndrawSound(container);
        }

        public void OnReleased(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            var container = grabbable.GetComponentInParent<VobLoader>()?.Container;
            if (!IsWeapon(container))
                return;
            _vrWeaponService.PlayDrawSound(container);
        }

        private bool IsWeapon(VobContainer container)
        {
            var itemInstance = container?.GetItemInstance();
            if (itemInstance == null) return false;
            var flag = (VmGothicEnums.ItemFlags)itemInstance.MainFlag;
            return flag is VmGothicEnums.ItemFlags.ItemKatNf or VmGothicEnums.ItemFlags.ItemKatFf;
        }
    }
}
#endif
