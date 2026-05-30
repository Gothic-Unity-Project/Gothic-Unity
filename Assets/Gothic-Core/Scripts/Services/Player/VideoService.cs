using System.Collections.Generic;
using System.IO;
using System.Linq;
using GUZ.Core.Logging;
using GUZ.Core.Extensions;
using GUZ.Core.Services.Context;
using Reflex.Attributes;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Services.Player
{
    public class VideoService
    {
        public List<string> VideoFileNamesMp4 = new();
        public List<string> VideoFilePathsMp4 = new();

        
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;


        public void InitVideos()
        {
            var videoFileFolder = $"{_contextGameVersionService.RootPath}/_work/DATA/video/";

            if (!Directory.Exists(videoFileFolder))
            {
                Logger.LogError($"Video folder >{videoFileFolder}< not found!", LogCat.Loading);
                return;
            }

            VideoFilePathsMp4 = Directory.EnumerateFiles(videoFileFolder, "*.mp4").ToList();
            VideoFileNamesMp4 = VideoFilePathsMp4.Select(Path.GetFileName).ToList();

            if (VideoFilePathsMp4.IsEmpty())
            {
                Logger.LogWarning($"No MP4 videos found in the video folder at >{videoFileFolder}<.", LogCat.Loading);
            }
        }
    }
}
