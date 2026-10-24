#if GOTHIC_HVR_INSTALLED
using Gothic.VR.Interaction.Ports;
using Reflex.Core;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    public class HvrBackend : IVrBackend
    {
        public string Name => "HurricaneVR";

        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            containerBuilder.AddSingleton(typeof(HvrInput), typeof(IVrInput));
            containerBuilder.AddSingleton(typeof(HvrHaptics), typeof(IVrHaptics));
            containerBuilder.AddSingleton(typeof(HvrUiPointers), typeof(IVrUiPointers));
            containerBuilder.AddSingleton(typeof(HvrSfxPlayer), typeof(IVrSfxPlayer));
        }

        public bool OwnsRig(IVrRig rig)
        {
            return rig is HvrRig;
        }
    }
}
#endif
