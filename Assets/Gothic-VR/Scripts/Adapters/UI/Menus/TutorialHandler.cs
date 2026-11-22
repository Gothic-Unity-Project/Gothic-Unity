using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Vm;
using Gothic.Core;
using Gothic.Core.Adapters.UI.Menus;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UI;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI.Menus
{
    /// <summary>
    /// Shows the VR help panels when its Gothic logo is clicked and hides them on the next click.
    /// The panels take the menus' places: the menus in _hiddenWhileShown and the open Gothic menu
    /// (MenuHandler.CurrentMenu) are hidden meanwhile. The next MenuOpened (back button, menu reopened after a close)
    /// brings the menus back.
    /// </summary>
    public class TutorialHandler : MonoBehaviour
    {
        [SerializeField] private Image[] _backgroundImages;

        [Tooltip("The Gothic logo whose clicks show and hide the panels.")]
        [SerializeField] private GameObject _logo;
        [Tooltip("The help panels to show and hide (Quest2, Hero Slots, Help).")]
        [SerializeField] private GameObject[] _panels;
        [Tooltip("Side menus hidden while the panels are shown (status, quest log). The open Gothic menu is " +
                 "always hidden. Empty in the main menu scene.")]
        [SerializeField] private GameObject[] _hiddenWhileShown;

        [Inject] private readonly VmService _vmService;
        [Inject] private readonly TextureService _textureService;

        private bool _isShown;
        private MenuHandler _menuHandler;
        private GameObject _hiddenMenu;


        private void Awake()
        {
            SetActive(_panels, false);

            // MenuHandler opens its first menu before this Awake (it activates first), so it's asked on show.
            _menuHandler = GetComponentInParent<MenuHandler>(true);

            GlobalEventDispatcher.GothicLogoClicked.AddListener(OnGothicLogoClicked);
            GlobalEventDispatcher.MenuOpened.AddListener(OnMenuOpened);
        }

        private void Start()
        {
            var backPic = _textureService.GetMaterial(_vmService.BackPic);

            foreach (var image in _backgroundImages)
            {
                image.material = backPic;
            }

            // Shown in the menu, which is drawn on top of the world: lift the new materials as well.
            var onTop = GetComponentInParent<VRUiOnTop>();
            if (onTop != null)
                onTop.Apply();
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.GothicLogoClicked.RemoveListener(OnGothicLogoClicked);
            GlobalEventDispatcher.MenuOpened.RemoveListener(OnMenuOpened);
        }

        private void OnGothicLogoClicked(GameObject logo)
        {
            if (logo != _logo)
                return;

            _isShown = !_isShown;
            SetActive(_panels, _isShown);
            SetActive(_hiddenWhileShown, !_isShown);

            if (_isShown)
            {
                _hiddenMenu = _menuHandler != null ? _menuHandler.CurrentMenu : null;
                if (_hiddenMenu != null)
                    _hiddenMenu.SetActive(false);
            }
            else
            {
                if (_hiddenMenu != null)
                    _hiddenMenu.SetActive(true);
                _hiddenMenu = null;
            }

            Logger.Log($"[TutorialHandler] Help panels {(_isShown ? "shown" : "hidden")}", LogCat.Ui);
        }

        /// <summary>
        /// MenuHandler activated the new menu itself: only the panels and side menus switch back.
        /// </summary>
        private void OnMenuOpened(GameObject menu)
        {
            if (!_isShown)
                return;

            _isShown = false;
            _hiddenMenu = null;
            SetActive(_panels, false);
            SetActive(_hiddenWhileShown, true);

            Logger.Log("[TutorialHandler] Help panels hidden, menu opened", LogCat.Ui);
        }

        private static void SetActive(GameObject[] gameObjects, bool isActive)
        {
            foreach (var go in gameObjects)
            {
                if (go != null)
                    go.SetActive(isActive);
            }
        }
    }
}
