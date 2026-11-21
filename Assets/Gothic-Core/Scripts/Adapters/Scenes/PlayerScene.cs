using Gothic.Core.Adapters.UI.Achievements;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services;
using Gothic.Core.Services.Achievements;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Scenes
{
    public class PlayerScene : MonoBehaviour, IScene
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly BootstrapService _bootstrapService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly AchievementService _achievementService;
        
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
        /// One achievement toast for the whole game (VR: Prefab Variant in front of the camera). None while
        /// achievements are off.
        /// </summary>
        private void CreateAchievementToast()
        {
            GlobalEventDispatcher.ZenKitBootstrapped.RemoveListener(CreateAchievementToast);
            if (!_achievementService.IsEnabled)
                return;

            var toast = _resourceCacheService.TryGetPrefabObject(PrefabType.UiAchievementToast, parent: gameObject)
                ?.GetComponent<AchievementToast>();
            if (toast == null)
            {
                Logger.LogWarning("[Achievements] No achievement toast prefab (with AchievementToast) - unlocks " +
                                  "show no notice.", LogCat.Achievements);
                return;
            }

            toast.Init();
        }
    }
}
