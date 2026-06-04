using Gothic.Core.Adapters.UI.LoadingBars;
using Gothic.Core.Manager;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.World;
using Gothic.Core.Const;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Adapters.Scenes
{
    public class LoadingScene : MonoBehaviour, IScene
    {
        [SerializeField] private AbstractLoadingBarHandler _loadingBarHandler;
        
        
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly LoadingService _loadingService;
        [Inject] private readonly BootstrapService _bootstrapService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        
        
        public void Init()
        {
            _loadingService.InitLoading(_loadingBarHandler);

            _contextInteractionService.TeleportPlayerTo(_loadingBarHandler.transform.position);
            
            GlobalEventDispatcher.LoadingSceneLoaded.Invoke();

            // Start loading world!
            _bootstrapService.LoadScene(_saveGameService.CurrentWorldName);
        }
    }
}
