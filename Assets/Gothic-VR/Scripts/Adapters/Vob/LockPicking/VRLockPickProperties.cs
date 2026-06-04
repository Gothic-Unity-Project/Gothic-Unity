#if GOTHIC_HVR_INSTALLED
using UnityEngine;

namespace Gothic.VR.Adapters.Vob.LockPicking
{
    public class VRLockPickProperties : VRVobItemProperties
    {
        public bool IsInsideLock;
        public VRContainerDoorPickingInteraction ActiveContainerDoorPicking;
        public Transform HoldingHand;
    }
}
#endif
