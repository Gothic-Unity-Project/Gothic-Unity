using GUZ.Core.Const;
using GUZ.Core.Models.Config;
using GUZ.Core.Services;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Context;
using GUZ.Core.Services.Meshes;
using GUZ.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Adapters.Scenes
{
    /// <summary>
    /// Specific manager for MainMenu.unity scene tasks only.
    /// </summary>
    public class MainMenuScene : MonoBehaviour, IScene
    {
        [SerializeField] private GameObject _mainMenuImageBackground;

        
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly TextureService _textureService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly BootstrapService _bootstrapService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        
        
        public void Init()
        {
            _contextInteractionService.InitUIInteraction();

            if (!_configService.Dev.EnableMainMenu)
            {
                // We need to invoke this event, even when we skip MainMenu (for event listeners, main menu is 'loaded')
                GlobalEventDispatcher.MainMenuSceneLoaded.Invoke();

                if (_configService.Dev.LoadFromSaveSlot)
                {
                    var saveId = (SaveGameService.SlotId)_configService.Dev.SaveSlotToLoad;
                    var save = _saveGameService.GetSaveGame(saveId);
                    _bootstrapService.LoadWorld(save.Metadata.World, saveId, Constants.SceneMainMenu);
                }
                else
                {
                    // Load New game at certain world
                    _bootstrapService.LoadWorld(GetWorldNameToSpawn(), 0, Constants.SceneMainMenu);
                }
                return;
            }
            
            // We set the gothic background image in MainMenu with this material.
            _mainMenuImageBackground.GetComponent<MeshRenderer>().material = _textureService.MainMenuImageBackgroundMaterial;

            _contextInteractionService.TeleportPlayerTo(Vector3.zero);
            _contextInteractionService.DisableMenus();

            GlobalEventDispatcher.MainMenuSceneLoaded.Invoke();
        }

        private string GetWorldNameToSpawn()
        {
            var world = _configService.Dev.PreselectWorldToSpawn;

            if (world == DeveloperConfigEnums.WorldToSpawn.None)
                return _configService.GothicMod.World;
            else
                return DeveloperConfigEnums.WorldMappings[world];
        }
    }
}
