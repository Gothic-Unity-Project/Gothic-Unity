using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Model.UI.Menu;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.UI.Menus
{
    public class MenuHandler : MonoBehaviour
    {
        [Inject] private readonly ContextMenuService _contextMenuService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly BootstrapService _bootstrapService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        private Dictionary<string, GameObject> _menuList = new();
        private string _currentMenu;
        private Stack<string> _menuQueue = new();

        // Cached MainMenu tree hierarchy of submenus (load, save, settings) and their children (sub-settings, settings fields)
        public AbstractMenuInstance MainMenuHierarchy { get; private set; }

        private void Awake()
        {
            InitializeMenus();
        }

        private void OnEnable()
        {
            OpenMenu("MENU_MAIN");
        }

        private void InitializeMenus()
        {
            // Initialize whole ZenKit Menu.dat hierarchy.
            MainMenuHierarchy = new MenuInstanceAdapter("MENU_MAIN", null);

            _contextMenuService.UpdateMainMenu(MainMenuHierarchy);
            
            InstantiateMenus();

            _contextInteractionService.InitUIInteraction();
            
            CloseAllMenus();
        }

        private void InstantiateMenus()
        {
            var menuInstanceNames = MainMenuHierarchy.GetMenuInstanceNamesRecursive();
            Logger.Log($"[MenuHandler] Instantiating {menuInstanceNames.Count} menus: {string.Join(", ", menuInstanceNames)}", LogCat.Ui);

            foreach (var menuName in menuInstanceNames)
            {
                var go = _resourceCacheService.TryGetPrefabObject($"Prefabs/UI/Menus/{menuName}", parent: this.gameObject, worldPositionStays: false);

                if (go == null)
                {
                    Logger.LogWarning($"[MenuHandler] No prefab for >{menuName}< — skipping (menu will be unavailable)", LogCat.Ui);
                    continue;
                }

                try
                {
                    go.GetComponent<AbstractMenu>().InitializeMenu(MainMenuHierarchy.FindMenuRecursive(menuName));
                    _menuList.Add(menuName, go);
                }
                catch (System.Exception e)
                {
                    Logger.LogError($"[MenuHandler] Failed to initialize >{menuName}<: {e.Message}", LogCat.Ui);
                    Object.Destroy(go);
                }
            }

            Logger.Log($"[MenuHandler] Instantiated {_menuList.Count}/{menuInstanceNames.Count} menus successfully", LogCat.Ui);
        }
        
        private void InstantiateMenu(string menuName, GameObject prefab)
        {
            var go = Instantiate(prefab, transform);
            go.GetComponent<AbstractMenu>().InitializeMenu(MainMenuHierarchy.FindMenuRecursive(menuName));
            
            _menuList.Add(menuName, go);
        }

        public void ToggleVisibility()
        {
            // reset the queue
            if (gameObject.activeSelf)
            {
                CloseAllMenus();
                _menuQueue.Clear();
                _currentMenu = null;
            }

            gameObject.SetActive(!gameObject.activeSelf);
        }

        public void OpenMenu(string menuName, bool viaBackButton = false)
        {
            if (!_menuList.ContainsKey(menuName))
            {
                // G2's MENU_NEW_GAME (and G1 mods' custom equivalents — Dolina Zombie's
                // MENU_WIRKLICH_NEUES, Świat Skazańców' MENU_PLAY_SELECT, New Balance's MENU_NG) is
                // only a yes/no confirmation dialog (headline + yes + no) and we have no prefab for
                // it — treat opening it as an immediate "yes" and start the new game right away (same
                // call MainMenu.Close makes for G1's direct NEW_GAME action). The set of compatible
                // mods is finite in practice, so a plain name list is simpler than generic detection.
                if (menuName is "MENU_NEW_GAME" or "MENU_WIRKLICH_NEUES" or "MENU_PLAY_SELECT" or "MENU_NG")
                {
                    // GothicMod.World defaults to G1's "World.zen" when no mod ini is loaded — for a
                    // vanilla G2 install the start world is NewWorld.zen instead.
                    var world = !_configService.GothicMod.IsLoaded && _contextGameVersionService.IsGothic2()
                        ? "NewWorld.zen"
                        : _configService.GothicMod.World;
                    ToggleVisibility();
                    _bootstrapService.LoadWorld(world, 0, SceneManager.GetActiveScene().name);
                    return;
                }

                // Menu exists in MENU.DAT but couldn't be instantiated (no prefab / init failure).
                // Stay on the currently open menu instead of closing everything into a dead screen.
                Logger.LogWarning($"[MenuHandler] Menu '{menuName}' is unavailable — staying on '{_currentMenu}'", LogCat.Ui);
                return;
            }

            if (!viaBackButton && !_currentMenu.IsNullOrEmpty())
            {
                _menuQueue.Push(_currentMenu);
            }

            _currentMenu = menuName;

            CloseAllMenus();
            _menuList[menuName].SetActive(true);
        }

        private void CloseAllMenus()
        {
            foreach (var menu in _menuList.Values)
            {
                menu.SetActive(false);
            }
        }

        public void BackMenu()
        {
            var nextMenu = _menuQueue.TryPop(out string result);
            if (nextMenu)
            {
                OpenMenu(result, true);
            }
            else
            {
                ToggleVisibility();
            }
        }
    }
}
