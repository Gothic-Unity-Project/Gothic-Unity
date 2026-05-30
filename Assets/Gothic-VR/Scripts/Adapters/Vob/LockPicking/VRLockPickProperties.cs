#if GUZ_HVR_INSTALLED
using UnityEngine;

namespace GUZ.VR.Adapters.Vob.LockPicking
{
    public class VRLockPickProperties : VRVobItemProperties
    {
        public bool IsInsideLock;
        public VRContainerDoorPickingInteraction ActiveContainerDoorPicking;
        public Transform HoldingHand;
    }
}
#endif
