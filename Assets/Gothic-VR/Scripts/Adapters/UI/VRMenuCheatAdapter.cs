#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Adapters.UI.Menus;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.VR.Adapters.HVROverrides;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI
{
    public class VRMenuCheatAdapter : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;

        private MenuHandler _menuHandler;
        private StatusMenu _statusMenu;
        private bool _initialized;

        private int _levelClicks;
        private int _guildClicks;
        private bool _wasMenuActive;

        private void Update()
        {
            if (_configService == null) return;
            if (!_configService.Dev.EnableLevel5Cheat && !_configService.Dev.EnableGuildCheat) return;

            if (!_initialized) TryInit();
            if (!_initialized) return;

            if (_statusMenu == null)
                _statusMenu = _menuHandler.GetComponentInChildren<StatusMenu>(true);
            if (_statusMenu == null) return;

            var isActive = _statusMenu.gameObject.activeSelf;

            if (_wasMenuActive && !isActive)
            {
                Logger.Log($"[VRMenuCheatAdapter] Menu closed — reset (L:{_levelClicks} G:{_guildClicks})", LogCat.Ui);
                _levelClicks = 0;
                _guildClicks = 0;
            }
            _wasMenuActive = isActive;

            if (!isActive) return;

            if (_configService.Dev.EnableLevel5Cheat && LeftPrimaryJustPressed())
            {
                _levelClicks++;
                Logger.Log($"[VRMenuCheatAdapter] Left click #{_levelClicks}/5", LogCat.Ui);
                if (_levelClicks >= 5)
                {
                    _levelClicks = 0;
                    _statusMenu.ExecuteLevelCheat();
                }
            }

            if (_configService.Dev.EnableGuildCheat && RightPrimaryJustPressed())
            {
                _guildClicks++;
                Logger.Log($"[VRMenuCheatAdapter] Right click #{_guildClicks}/3", LogCat.Ui);
                if (_guildClicks >= 3)
                {
                    _guildClicks = 0;
                    _statusMenu.ExecuteGuildCheat();
                }
            }
        }

        private void TryInit()
        {
            var player = GetComponent<VRPlayerController>();
            if (player == null) player = GetComponentInParent<VRPlayerController>();
            if (player == null) player = FindAnyObjectByType<VRPlayerController>();
            if (player == null) return;
            _menuHandler = player.MenuHandler;
            if (_menuHandler == null) return;
            _initialized = true;
            Logger.Log($"[VRMenuCheatAdapter] Initialized — MenuHandler: {_menuHandler.name}", LogCat.Ui);
        }

        private static bool LeftPrimaryJustPressed()
        {
            if (Keyboard.current != null && Keyboard.current[Key.K].wasPressedThisFrame)
            {
                Logger.Log("[VRMenuCheatAdapter] K key (sim left)", LogCat.Ui);
                return true;
            }
            return HVRController.GetButtonState(HVRHandSide.Left, HVRButtons.Primary).JustActivated;
        }

        private static bool RightPrimaryJustPressed()
        {
            if (Keyboard.current != null && Keyboard.current[Key.L].wasPressedThisFrame)
            {
                Logger.Log("[VRMenuCheatAdapter] L key (sim right)", LogCat.Ui);
                return true;
            }
            return HVRController.GetButtonState(HVRHandSide.Right, HVRButtons.Primary).JustActivated;
        }
    }
}
#endif
