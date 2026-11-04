using System;
using System.Collections.Generic;
using Gothic.Core.Adapters.UI.Menus;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Config;
using Gothic.Core.Models.Context;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Scenes
{
    public class GameVersionScene : MonoBehaviour, IScene
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly BootstrapService _bootstrapService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;

        [SerializeField] private GameObject _invalidInstallationDir;
        [SerializeField] private GameProfileMenu _gameProfileMenu;


        private void Awake()
        {
            // Just in case we forgot to disable them in scene view. ;-)
            _invalidInstallationDir.SetActive(false);
            _gameProfileMenu.gameObject.SetActive(false);
        }

        public void Init()
        {
            /*
             * 1. DeveloperConfig PreselectGameVersion (+ PreSelectMod): start it directly.
             * 2. Otherwise check the G1 + G2 installations of GameSettings.json.
             * 3. Each found game offers each game ini of its system/ folder (vanilla GothicGame.ini and installed mods).
             * 4. Exactly one of them: start it directly.
             * 5. More: show them in the GameProfileMenu. None: show the error message.
             */

            // Back onto the scene's floor (at 0, like MainMenu) - the player falls while this scene loads async, as the
            // Player scene has no floor.
            _contextInteractionService.TeleportPlayerTo(Vector3.zero);

            // Whatever comes next, we don't want the player to move around right now.
            _contextInteractionService.LockPlayer(PlayerLocks.Loading);

            if (_configService.TryGetPreselectedGameVersion(out var preselectedVersion))
            {
                if (_configService.CheckIfGothicInstallationExists(preselectedVersion))
                {
                    Logger.Log($"Starting preselected {preselectedVersion}.", LogCat.Loading);
                    StartGame(preselectedVersion);
                }
                else
                {
                    // If the Gothic installation directory is not set, show an error message and exit.
                    _invalidInstallationDir.SetActive(true);
                    throw new ArgumentException($"{preselectedVersion} installation couldn't be found inside " +
                                                ">GameSettings.json< file.");
                }

                return;
            }

            var profiles = _configService.FindGameProfiles();
            if (profiles.Count == 0)
            {
                Logger.LogWarning("No installation of Gothic1 nor Gothic2 found.", LogCat.Loading);
                _invalidInstallationDir.SetActive(true);
            }
            else if (profiles.Count == 1)
            {
                Logger.Log($"Only >{profiles[0].Name}< found - starting it.", LogCat.Loading);
                StartGameProfile(profiles[0]);
            }
            else
            {
                Logger.Log($"Showing game profile menu with {profiles.Count} profile(s).", LogCat.Loading);
                ShowMenu(profiles);
            }
        }

        private void ShowMenu(IReadOnlyList<GameProfile> profiles)
        {
            _gameProfileMenu.gameObject.SetActive(true);
            _gameProfileMenu.Build(profiles, StartGameProfile);

            // Registers the new canvas for the VR laser pointer.
            _contextInteractionService.InitUIInteraction();
        }

        private void StartGameProfile(GameProfile profile)
        {
            _configService.SelectGameProfile(profile);
            StartGame(profile.Version);
        }

        private void StartGame(GameVersion version)
        {
            // The static cache is kept per (game, mod) and created if missing.
            _bootstrapService.InitPhase2(version);
            _bootstrapService.LoadScene(Constants.ScenePreCaching, Constants.SceneGameVersion);
        }
    }
}
