#if GOTHIC_HVR_INSTALLED
using HurricaneVR.Framework.Core.Grabbers;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// Added by VRBow: while the bow is held (VRBow exists), the other hand can't grab the bow itself - its grip near the
    /// string draws the string instead (the bow's colliders cover the string, HVR would dual-grab the bow).
    /// </summary>
    public class VRBowStringFilter : HVRHandGrabFilter
    {
        public override bool CanBeGrabbed(HVRHandGrabber hand)
        {
            if (Grabbable == null || !TryGetComponent<VRBow>(out _))
                return true;
            return !Grabbable.IsHandGrabbed || Grabbable.HandGrabbers.Contains(hand);
        }
    }
}
#endif
