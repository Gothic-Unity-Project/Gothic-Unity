using Gothic.Core;
using Gothic.VR.Interaction.Backends.Xri;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Core;
using UnityEngine;

namespace Gothic.VR
{
    /// <summary>
    /// Will be automatically called by Reflex when scene is loaded and SceneScope.component is added to the scene.
    /// </summary>
    public class ReflexVRSceneInstaller : MonoBehaviour, IInstaller
    {
        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            containerBuilder.OnContainerBuilt += (container) => ReflexProjectInstaller.DIContainer = container;
            containerBuilder.OnContainerBuilt += (container) => container.Resolve<VRCarriedItemsSaveService>().Init();
            containerBuilder.OnContainerBuilt += (container) => container.Resolve<VrFavoritesService>().Init();
            containerBuilder.OnContainerBuilt += (container) => container.Resolve<VRHeroBlockService>().Init();

            // The VR ports are implemented by the XR Interaction Toolkit. IVrRig lives on the player prefab.
            containerBuilder.AddSingleton(typeof(XriInput), typeof(IVrInput), typeof(XriInput));
            containerBuilder.AddSingleton(typeof(XriHaptics), typeof(IVrHaptics));
            containerBuilder.AddSingleton(typeof(XriUiPointers), typeof(IVrUiPointers));
            containerBuilder.AddSingleton(typeof(XriSfxPlayer), typeof(IVrSfxPlayer));

            containerBuilder.AddSingleton(typeof(VRPlayerService));
            containerBuilder.AddSingleton(typeof(VRMobUseService));
            containerBuilder.AddSingleton(typeof(VRWeaponService));
            containerBuilder.AddSingleton(typeof(VRHeldItemSyncService));
            containerBuilder.AddSingleton(typeof(VrHapticsService));
            containerBuilder.AddSingleton(typeof(VRRangedService));
            containerBuilder.AddSingleton(typeof(VRSpellTransformService));
            containerBuilder.AddSingleton(typeof(VrDropZoneService));
            containerBuilder.AddSingleton(typeof(VrLockTouchService));
            containerBuilder.AddSingleton(typeof(VRCinemaService));
            containerBuilder.AddSingleton(typeof(VRMarvinPanelService));
            containerBuilder.AddSingleton(typeof(VRUiOnTopService));
            containerBuilder.AddSingleton(typeof(VRGameViewFallbackService));
            containerBuilder.AddSingleton(typeof(VRCarriedItemsSaveService));
            containerBuilder.AddSingleton(typeof(VRHeroBlockService));
            containerBuilder.AddSingleton(typeof(VrFavoritesService));
            containerBuilder.AddSingleton(typeof(VRPickpocketService));
        }
    }
}
