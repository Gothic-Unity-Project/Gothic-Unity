#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.Components;
using Reflex.Attributes;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Chest lid (HVR physics door). Opening it shows the chest's slots (VrChestSlots, which spawns the content on
    /// the first open), closing it hides them.
    /// Temporarily in the HVR backend as it subclasses HVRPhysicsDoor (VrHinge in Phase 4). On other backends it
    /// switches itself off before the door is set up (VrChestLidToggle is the lid there).
    /// </summary>
    public class HvrPhysicsChest : HVRPhysicsDoor
    {
        [Inject] private readonly IVrBackend _vrBackend;

        private VrChestSlots _chestSlots;

        // Flags to ensure we always call On*() once, everytime open/close status changes.
        private bool _onOpenedHandled;
        private bool _onClosedHandled;


        public override void Start()
        {
            if (_vrBackend != null && _vrBackend is not HvrBackend)
            {
                enabled = false;
                return;
            }

            base.Start();

            _chestSlots = GetComponentInParent<VrChestSlots>();
            if (_chestSlots == null)
                Logger.LogError($"[HVR] Chest lid {name} has no VrChestSlots in its parents.", LogCat.VR);
        }

        protected override void Update()
        {
            base.Update();

            if (Opened && !_onOpenedHandled)
            {
                OnOpened();
            }

            if (Closed && !_onClosedHandled)
            {
                OnClosed();
            }
        }

        private void OnOpened()
        {
            if (_chestSlots != null)
                _chestSlots.Open();

            _onOpenedHandled = true;
            _onClosedHandled = false;
        }

        private void OnClosed()
        {
            if (_chestSlots != null)
                _chestSlots.Close();

            _onOpenedHandled = false;
            _onClosedHandled = true;
        }
    }
}
#endif
