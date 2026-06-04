using Gothic.Core.Const;
using Gothic.Core.Services;
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
        
        public void Init()
        {
            _bootstrapService.InitPhase1();

            _contextInteractionService.SetupPlayerController(_configService.Dev);

            GlobalEventDispatcher.PlayerSceneLoaded.Invoke();

            _bootstrapService.LoadScene(Constants.SceneGameVersion);
        }
    }
}
