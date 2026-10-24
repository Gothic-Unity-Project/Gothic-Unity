using Gothic.Core;
using Gothic.Core.Services.Config;
using Gothic.VR.Adapters.Context;
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

            // VR ports (IVrInput, IVrHaptics, ...) come from the active backend (DeveloperConfig.VrInteractionBackend).
            // At this point, DIContainer is still the parent (project) container.
            var developerConfig = ReflexProjectInstaller.DIContainer?.Resolve<ConfigService>().Dev;
            var backend = VRContextBootstrap.SelectBackend(developerConfig);
            backend.InstallBindings(containerBuilder);
            containerBuilder.AddSingleton(backend, typeof(IVrBackend));

            containerBuilder.AddSingleton(typeof(VRPlayerService));
            containerBuilder.AddSingleton(typeof(VRWeaponService));
            containerBuilder.AddSingleton(typeof(VrHapticsService));
            containerBuilder.AddSingleton(typeof(VRCinemaService));
            containerBuilder.AddSingleton(typeof(VRUiOnTopService));
        }
    }
}
