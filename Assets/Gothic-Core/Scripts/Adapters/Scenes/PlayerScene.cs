using Gothic.Core.Adapters.UI.Achievements;
using Gothic.Core.Const;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Adapters.Scenes
{
    public class PlayerScene : MonoBehaviour, IScene
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly BootstrapService _bootstrapService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        
        public void Init()
        {
            _bootstrapService.InitPhase1();

            _contextInteractionService.SetupPlayerController(_configService.Dev);

            // Prefabs load once the Gothic installation is mounted (InitPhase2).
            GlobalEventDispatcher.ZenKitBootstrapped.AddListener(CreateAchievementToast);

            GlobalEventDispatcher.PlayerSceneLoaded.Invoke();

            _bootstrapService.LoadScene(Constants.SceneGameVersion);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.ZenKitBootstrapped.RemoveListener(CreateAchievementToast);
        }

        /// <summary>
        /// One achievement toast for the whole game (VR: Prefab Variant in front of the camera).
        /// </summary>
        private void CreateAchievementToast()
        {
            GlobalEventDispatcher.ZenKitBootstrapped.RemoveListener(CreateAchievementToast);
            _resourceCacheService.TryGetPrefabObject(PrefabType.UiAchievementToast, parent: gameObject)!
                .GetComponent<AchievementToast>().Init();
        }
    }
}
