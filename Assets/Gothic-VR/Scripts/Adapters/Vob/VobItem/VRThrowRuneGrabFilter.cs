#if GOTHIC_HVR_INSTALLED
using HurricaneVR.Framework.Core.Grabbers;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// DeveloperConfig.EnableThrowRuneOneHand: a rune with a throwable spell (fire ball, ice lance) is held in one hand,
    /// the other hand throws. Squeezing grip + trigger to throw (like a fist) grabbed the rune with the throwing hand
    /// when it was close to it - both hands on the rune left no hand to throw with and the spell never came.
    /// </summary>
    public class VRThrowRuneGrabFilter : HVRHandGrabFilter
    {
        public override bool CanBeGrabbed(HVRHandGrabber hand)
        {
            return Grabbable == null || !Grabbable.IsHandGrabbed || Grabbable.HandGrabbers.Contains(hand);
        }
    }
}
#endif
