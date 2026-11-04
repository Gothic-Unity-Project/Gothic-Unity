using Gothic.Core.Adapters.UI.Menus;
using Gothic.Core.Const;
using Gothic.Core.Services;
using Gothic.VR.Adapters.UI;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Gothic side of the VR player prefab, next to the backend's IVrRig. Opens the in-game menu and provides the
    /// Gothic-specific inputs derived from IVrInput.
    /// </summary>
    public class VRPlayer : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly GameStateService _gameStateService;

        [Tooltip("In-game menu (skills, notepad, ...). Toggled with the menu button in game scenes.")]
        public MenuHandler MenuHandler;

        [Tooltip("Plays videos in front of the player.")]
        public VRCinema Cinema;

        [Tooltip("Step height while in water. 1m means walking out of water in Xardas' old tower in G1.")]
        public float SwimDiveStepHeight = 1f;

        [Tooltip("Disabled during dialogs, videos, ...")]
        public bool IsMenuButtonEnabled = true;

        /// <summary>
        /// The menu button was pressed this frame (and isn't disabled).
        /// </summary>
        public bool IsMenuActivated
        {
            get
            {
                if (!IsMenuButtonEnabled)
                    return false;

                if (_vrInput.IsSimulator)
                    return Keyboard.current[Key.Escape].wasPressedThisFrame;

                return _vrInput.GetButton(VrHandSide.Left, VrButton.Secondary).IsJustActivated;
            }
        }

        /// <summary>
        /// Both grips are held.
        /// </summary>
        public bool IsBothGripsActive
        {
            get
            {
                if (_vrInput.IsSimulator && _vrInput.UsesBothGripsSimulatorKey)
                    return Keyboard.current[Key.T].isPressed;

                return _vrInput.GetButton(VrHandSide.Left, VrButton.Grip).IsActive
                       && _vrInput.GetButton(VrHandSide.Right, VrButton.Grip).IsActive;
            }
        }

        private void Start()
        {
            // Enabled later via button press or other events
            MenuHandler?.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (IsMenuActivated && IsGameScene() && (Cinema == null || !Cinema.IsBlockingInput))
            {
                _gameStateService.InGameAndAlive = true;
                MenuHandler.ToggleVisibility();
            }
        }

        /// <summary>
        /// Game scenes are all the ones where we play. Aka !=MainMenu, !=Loadings, !=Logo videos, ...
        /// </summary>
        private bool IsGameScene()
        {
            var activeSceneName = SceneManager.GetActiveScene().name;

            return activeSceneName switch
            {
                Constants.SceneBootstrap => false,
                Constants.ScenePlayer => false,
                Constants.SceneGameVersion => false,
                Constants.ScenePreCaching => false,
                Constants.SceneLogo => false,
                Constants.SceneMainMenu => false,
                Constants.SceneLoading => false,
                _ => true
            };
        }
    }
}
