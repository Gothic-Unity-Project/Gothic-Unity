#if GOTHIC_HVR_INSTALLED
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.Components;
using Reflex.Attributes;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Chest lid (HVR physics door). Opening/closing is reported by HvrHingeBridge (IVrHinge.Opened/Closed), which
    /// VrChestSlots listens to. Subclasses HVRPhysicsDoor only so that it can switch itself off before the door is
    /// set up if another VR backend runs (VrHinge is the lid there).
    /// </summary>
    public class HvrPhysicsChest : HVRPhysicsDoor
    {
        [Inject] private readonly IVrBackend _vrBackend;

        public override void Start()
        {
            if (_vrBackend != null && _vrBackend is not HvrBackend)
            {
                enabled = false;
                return;
            }

            base.Start();
        }
    }
}
#endif
