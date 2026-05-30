using GUZ.Core.Adapters.UI.LoadingBars;
using GUZ.Core.Const;
using GUZ.Core.Manager;
using GUZ.Core.Services;
using GUZ.Core.Services.Context;
using GUZ.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Adapters.Scenes
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
