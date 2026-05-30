using GUZ.Core.Creator;
using GUZ.Core.Domain.Vobs;
using GUZ.Core.Manager;
using GUZ.Core.Models.Container;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Context;
using GUZ.Core.Services.Culling;
using GUZ.Core.Services.Meshes;
using GUZ.Core.Services.Npc;
using GUZ.Core.Services.Player;
using GUZ.Core.Services.StaticCache;
using GUZ.Core.Services.UI;
using GUZ.Core.Services.Vm;
using GUZ.Core.Services.Vobs;
using GUZ.Core.Services.World;
using GUZ.Services.UI;
using Reflex.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GUZ.Core
{
    public class ReflexProjectInstaller : MonoBehaviour, IInstaller
    {
        public static Container DIContainer;


        /// <summary>
        /// As of now, we have two scopes where we set up Service - DI. By default, Reflex always inherits every SceneScope from ProjectScope.
        /// But in our case, Scene World.scene requires us to inherit Player.scene as we e.g., inject Vobs with VR behaviour in this scene.
        /// But the VR injects are defined in Player.scene scope.
        ///
        /// Lookup path goes like this:
        /// * OtherScopes
        ///       ^^^
        /// * PlayerScope (VR/Flat)
        ///       ^^^
        /// * ProjectScope (Bootstrap)
        /// </summary>
        public void OverrideParent(Scene scene, ContainerBuilder builder)
        {
            // If we have the PlayerScope already registered, then inherit every new SceneScope from it.
            if (DIContainer.Name.StartsWith("Player"))
                builder.SetParent(DIContainer);
        }

        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            SceneScope.OnSceneContainerBuilding += OverrideParent;
            containerBuilder.OnContainerBuilt += (container) => DIContainer = container;

            containerBuilder.AddSingleton(typeof(BootstrapService));
            containerBuilder.AddSingleton(typeof(GameStateService));
            
            containerBuilder.AddSingleton(typeof(UnityMonoService));
            containerBuilder.AddSingleton(typeof(ConfigService));
            containerBuilder.AddSingleton(typeof(FrameSkipperService));
            containerBuilder.AddSingleton(typeof(VmExternalService));
            containerBuilder.AddSingleton(typeof(VmService));
            containerBuilder.AddSingleton(typeof(ContextInteractionService));
            containerBuilder.AddSingleton(typeof(ContextMenuService));
            containerBuilder.AddSingleton(typeof(ContextDialogService));
            containerBuilder.AddSingleton(typeof(ContextGameVersionService));

            containerBuilder.AddSingleton(typeof(NpcMeshCullingService));
            containerBuilder.AddSingleton(typeof(VobMeshCullingService));
            containerBuilder.AddSingleton(typeof(VobSoundCullingService));
            containerBuilder.AddSingleton(typeof(SpeechToTextService));
            containerBuilder.AddSingleton(typeof(GameTimeService));
            containerBuilder.AddSingleton(typeof(MeshService));
            containerBuilder.AddSingleton(typeof(AnimationService));

            // Vob
            containerBuilder.AddSingleton(typeof(VobService));
            containerBuilder.AddSingleton(typeof(ParticleService));
            
            // Player
            containerBuilder.AddSingleton(typeof(DialogService));
            containerBuilder.AddSingleton(typeof(PlayerService));
            
            // NPC
            containerBuilder.AddSingleton(typeof(NpcService));
            containerBuilder.AddSingleton(typeof(NpcAiService));
            containerBuilder.AddSingleton(typeof(NpcHelperService));
            containerBuilder.AddSingleton(typeof(RoutineService));
            containerBuilder.AddSingleton(typeof(NpcRoutineService));
            containerBuilder.AddSingleton(typeof(NpcInventoryService));
            containerBuilder.AddSingleton(typeof(FightService));

            // Caches
            containerBuilder.AddSingleton(typeof(ResourceCacheService));
            containerBuilder.AddSingleton(typeof(VmCacheService));
            containerBuilder.AddSingleton(typeof(MultiTypeCacheService));
            containerBuilder.AddSingleton(typeof(TextureCacheService));
            containerBuilder.AddSingleton(typeof(MorphMeshCacheService));
            containerBuilder.AddSingleton(typeof(NpcArmorPositionCacheService));
            containerBuilder.AddSingleton(typeof(StaticCacheService));

            // World
            containerBuilder.AddSingleton(typeof(LoadingService));
            containerBuilder.AddSingleton(typeof(WayNetService));
            containerBuilder.AddSingleton(typeof(SaveGameService));
            containerBuilder.AddSingleton(typeof(StoryService));
            containerBuilder.AddSingleton(typeof(StationaryLightsService));
            containerBuilder.AddSingleton(typeof(PhysicsService));

            // Misc
            containerBuilder.AddSingleton(typeof(AudioService));
            containerBuilder.AddSingleton(typeof(VideoService));
            
            // Meshes
            containerBuilder.AddSingleton(typeof(TextureService));
            containerBuilder.AddSingleton(typeof(DynamicMaterialService));

            // UI
            containerBuilder.AddSingleton(typeof(FontService));
            containerBuilder.AddSingleton(typeof(UIEventsService));
            containerBuilder.AddSingleton(typeof(LocalizationService));

            // Marvin
            containerBuilder.AddSingleton(typeof(MarvinService));

            // FIXME - Need to be migrated to a Service!
            containerBuilder.AddSingleton(typeof(VobInitializerDomain));
            containerBuilder.AddSingleton(typeof(StationaryLightsService));
            containerBuilder.AddSingleton(typeof(SkyService));
            containerBuilder.AddSingleton(typeof(BarrierService));
            containerBuilder.AddSingleton(typeof(LoadingService));
        }
    }
}
