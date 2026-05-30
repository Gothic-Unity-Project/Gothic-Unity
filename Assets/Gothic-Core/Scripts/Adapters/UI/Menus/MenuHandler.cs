using System.Collections.Generic;
using GUZ.Core.Logging;
using GUZ.Core.Model.UI.Menu;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Context;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Adapters.UI.Menus
{
    public class MenuHandler : MonoBehaviour
    {
        [Inject] private readonly ContextMenuService _contextMenuService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

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

            foreach (var menuName in menuInstanceNames)
            {
                var go = _resourceCacheService.TryGetPrefabObject($"Prefabs/UI/Menus/{menuName}", parent: this.gameObject, worldPositionStays: false);

                if (go == null)
                {
                    Logger.LogError($"Could not find UI Menu prefab >{menuName}<", LogCat.Ui);
                    return;
                }

                go.GetComponent<AbstractMenu>().InitializeMenu(MainMenuHierarchy.FindMenuRecursive(menuName));
                _menuList.Add(menuName, go);
            }
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
            if (!viaBackButton && !_currentMenu.IsNullOrEmpty())
            {
                _menuQueue.Push(_currentMenu);
            }

            _currentMenu = menuName;

            CloseAllMenus();
            if (!_menuList.ContainsKey(menuName))
            {
                return;
            }

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
