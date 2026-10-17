using System;
using System.Text;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.UI;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Manager
{
    /// <summary>
    /// Marvin mode state and its activation like in the original game (status screen -> type "marvin" -> close):
    /// Open the menu, type "marvin" (or click the Gothic-Unity logo 3 times), close the menu.
    /// "42" (or the 3 clicks again) deactivates it.
    /// </summary>
    public class MarvinService
    {
        [Inject] private readonly ConfigService _configService;

        private const string _activationCode = "marvin";
        private const string _deactivationCode = "42";
        private const int _logoPokesToToggle = 3;
        private const int _maxTypedLength = 16;

        // Original texts. Shown top left like in Gothic.
        private const string _activatedMessage = "testmode on";
        private const string _deactivatedMessage = "WHAT WAS THE QUESTION?";
        private const string _messageFont = "FONT_OLD_20_WHITE.TGA";
        private const int _messagePos = 2;
        private const float _messageSeconds = 3f;

        public bool IsMarvinSelectionMode;
        public GameObject MarvinSelectionGO;

        private bool? _isMarvinMode;
        private readonly StringBuilder _typedText = new();
        private int _logoPokes;
        private bool _isMenuOpenedInGame;

        /// <summary>
        /// Toggled in game by every player. DeveloperConfig.PreselectMarvinMode is only a shortcut to start with it on.
        /// </summary>
        public bool IsMarvinMode => _isMarvinMode ??= _configService.Dev.PreselectMarvinMode;

        /// <summary>
        /// Whether typing and logo pokes currently count (menu open inside a game world).
        /// </summary>
        public bool IsActivationInputActive => _isMenuOpenedInGame;

        /// <summary>
        /// The last logo click was the one which toggles Marvin mode once the menu closes.
        /// </summary>
        public bool IsLogoPokeComplete => _isMenuOpenedInGame && _logoPokes == _logoPokesToToggle;

        public MarvinService()
        {
            GlobalEventDispatcher.MarvinLogoPoked.AddListener(_ =>
            {
                _logoPokes++;
            });
        }

        /// <summary>
        /// Like in the original game, Marvin mode can only be toggled in a game world, not in the main menu scene.
        /// </summary>
        public void OnMenuOpened()
        {
            _typedText.Clear();
            _logoPokes = 0;
            _isMenuOpenedInGame = SceneManager.GetActiveScene().name is not (Constants.SceneBootstrap
                or Constants.ScenePlayer or Constants.SceneGameVersion or Constants.ScenePreCaching
                or Constants.SceneLogo or Constants.SceneMainMenu or Constants.SceneLoading);
        }

        /// <summary>
        /// Text input also delivers control keys like Esc (closing the menu) and backspace.
        /// </summary>
        public void OnTextTyped(char c)
        {
            if (c == '\b')
            {
                if (_typedText.Length > 0)
                    _typedText.Remove(_typedText.Length - 1, 1);
                return;
            }

            if (char.IsControl(c))
                return;

            _typedText.Append(char.ToLowerInvariant(c));
            if (_typedText.Length > _maxTypedLength)
                _typedText.Remove(0, _typedText.Length - _maxTypedLength);
        }

        /// <summary>
        /// The input counts only once the menu is closed again, like in the original game.
        /// </summary>
        public void OnMenuClosed()
        {
            var typed = _typedText.ToString();
            var isPoked = _logoPokes == _logoPokesToToggle;
            var isMenuOpenedInGame = _isMenuOpenedInGame;
            _typedText.Clear();
            _logoPokes = 0;
            _isMenuOpenedInGame = false;

            if (!isMenuOpenedInGame)
                return;

            if (typed.EndsWith(_activationCode, StringComparison.Ordinal) || (isPoked && !IsMarvinMode))
                SetMarvinMode(true);
            else if (typed.EndsWith(_deactivationCode, StringComparison.Ordinal) || (isPoked && IsMarvinMode))
                SetMarvinMode(false);
        }

        private void SetMarvinMode(bool isActive)
        {
            _isMarvinMode = isActive;
            Logger.Log($"[MarvinService] Marvin mode {(isActive ? "activated" : "deactivated")}", LogCat.Ui);

            var message = isActive ? _activatedMessage : _deactivatedMessage;
            GlobalEventDispatcher.PrintScreen.Invoke(
                new PrintScreenMessage(message, _messagePos, _messagePos, _messageFont, _messageSeconds));
            GlobalEventDispatcher.MarvinModeChanged.Invoke(isActive);
        }
    }
}
