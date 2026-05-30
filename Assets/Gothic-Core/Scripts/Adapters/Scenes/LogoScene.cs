using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Services;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Player;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Video;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Adapters.Scenes
{
    public class LogoScene : MonoBehaviour , IScene
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VideoService _videoService;
        [Inject] private readonly BootstrapService _bootstrapService;

        [SerializeField] private VideoPlayer _videoPlayer;

        private Queue<string> _logoVideos = new();

        public void Init()
        {
            Logger.Log($"INI: playLogoVideos = {_configService.Gothic.IniPlayLogoVideos}", LogCat.Loading);
            if (_configService.Gothic.IniPlayLogoVideos)
            {
                _bootstrapService.LoadScene(Constants.SceneMainMenu, Constants.SceneLogo);
                return;
            }

            _videoPlayer.loopPointReached += LoadNextLogo;

            _logoVideos = new Queue<string>(_videoService.VideoFilePathsMp4.Where(i => i.StartsWithIgnoreCase("logo")));

            if (_logoVideos.IsEmpty())
            {
                Logger.Log("No logo videos in format .mp4 found. Skipping scene.", LogCat.Loading);
            }

            // Start first logo
            LoadNextLogo(null);
        }

        private void LoadNextLogo(VideoPlayer _)
        {
            if (_logoVideos.IsEmpty())
            {
                _bootstrapService.LoadScene(Constants.SceneMainMenu, Constants.SceneLogo);
                return;
            }
            
            _videoPlayer.url = _logoVideos.Dequeue();
            _videoPlayer.Play();
        }
    }
}
