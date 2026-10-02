#if GOTHIC_HVR_INSTALLED
using HurricaneVR.Framework.Core.Grabbers;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// Added by VRBow: while the bow is held (VRBow exists), the other hand near the string can't grab the bow - its grip
    /// draws the string instead. Elsewhere (limbs, grip) it grabs the bow, e.g. to hold it with both hands.
    /// </summary>
    public class VRBowStringFilter : HVRHandGrabFilter
    {
        public override bool CanBeGrabbed(HVRHandGrabber hand)
        {
            if (Grabbable == null || !TryGetComponent<VRBow>(out var bow))
                return true;
            if (!Grabbable.IsHandGrabbed || Grabbable.HandGrabbers.Contains(hand))
                return true;
            return !bow.IsAtString(hand.transform.position);
        }
    }
}
#endif
