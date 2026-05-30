using System;
using GUZ.Core.Const;
using GUZ.Core.Logging;
using GUZ.Core.Services;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Adapters.Scenes
{
    public class GameVersionScene : MonoBehaviour, IScene
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly BootstrapService _bootstrapService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;

        [SerializeField] private GameObject _invalidInstallationDir;


        private void Awake()
        {
            // Just in case we forgot to disable it in scene view. ;-)
            _invalidInstallationDir.SetActive(false);
        }

        public void Init()
        {
            /*
             * 1. Check for GameSettings if there are two valid game installations
             * 2. If there's none: Show error message!
             * 3. If there is one, skip this scene immediately
             * 4. If there are two, show selection between these two games. Once one is selected, call GUZContext.SetGameVersion(version)
             */

            // Whatever comes next, we don't want the player to move around right now.
            _contextInteractionService.LockPlayerInPlace();

            var isG1Installed = _configService.CheckIfGothicInstallationExists(GameVersion.Gothic1);
            var isG2Installed = _configService.CheckIfGothicInstallationExists(GameVersion.Gothic2);

            if (_configService.Dev.PreselectGameVersion)
            {
                var isInstalled = _configService.Dev.GameVersion == GameVersion.Gothic1 ? isG1Installed : isG2Installed;

                if (isInstalled)
                {
                    _bootstrapService.InitPhase2(_configService.Dev.GameVersion);
                    _bootstrapService.LoadScene(Constants.ScenePreCaching, Constants.SceneGameVersion);
                }
                else
                {
                    // If the Gothic installation directory is not set, show an error message and exit.
                    _invalidInstallationDir.SetActive(true);
                    throw new ArgumentException($"{_configService.Dev.GameVersion} installation couldn't be found inside >GameSettings.json< file.");
                }

                return;
            }

            // Neither is installed
            if (!isG1Installed && !isG2Installed)
            {
                Logger.LogWarning("No installation of Gothic1 nor Gothic2 found.", LogCat.Loading);

                _invalidInstallationDir.SetActive(true);
            }
            // Both are installed
            else if (isG1Installed && isG2Installed)
            {
                Logger.Log("Gothic1 and Gothic2 installation found.", LogCat.Loading);
            }
            // Only one is installed
            else
            {
                var version = isG1Installed ? GameVersion.Gothic1 : GameVersion.Gothic2;

                Logger.Log($"Installation for {version} found only.", LogCat.Loading);

                _bootstrapService.InitPhase2(version);
                _bootstrapService.LoadScene(Constants.SceneLogo, Constants.SceneGameVersion);
            }
        }
    }
}
