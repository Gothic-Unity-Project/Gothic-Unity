#if GOTHIC_HVR_INSTALLED
using HurricaneVR.Framework.Core.Grabbers;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// IVrInteractableBackend.IsOneHandOnly on HVR: while one hand holds the grabbable, the other one can't grab it
    /// (e.g. a throwable spell's rune - the other hand throws, DeveloperConfig.EnableThrowRuneOneHand).
    /// </summary>
    public class HvrOneHandGrabFilter : HVRHandGrabFilter
    {
        public override bool CanBeGrabbed(HVRHandGrabber hand)
        {
            return Grabbable == null || !Grabbable.IsHandGrabbed || Grabbable.HandGrabbers.Contains(hand);
        }
    }
}
#endif
