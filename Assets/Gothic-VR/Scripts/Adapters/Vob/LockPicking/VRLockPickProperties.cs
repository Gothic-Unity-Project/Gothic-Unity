using UnityEngine;

namespace Gothic.VR.Adapters.Vob.LockPicking
{
    public class VRLockPickProperties : VRVobItemProperties
    {
        public bool IsInsideLock;
        public VRContainerDoorPickingInteraction ActiveContainerDoorPicking;
        public Transform HoldingHand;
        // DeveloperConfig.EnableLockPickHandRoll: HoldingHand is the hand, turning is its roll.
        public bool IsHandRollTracked;
    }
}
