using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Logging;
using Gothic.Core.Services.Context;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Player
{
    public class VideoService
    {
        // Original Bink videos - played directly by BinkPlayer (no conversion needed).
        public List<string> VideoFilePathsBik = new();


        [Inject] private readonly ContextGameVersionService _contextGameVersionService;


        public void InitVideos()
        {
            // Any case: G2's folder is _work/Data/Video (Linux didn't find it).
            var videoFileFolder = PathExtension.FindIgnoringCase(_contextGameVersionService.RootPath, "_work/DATA/video");

            if (videoFileFolder == null || !Directory.Exists(videoFileFolder))
            {
                Logger.LogError($"Video folder >{_contextGameVersionService.RootPath}/_work/DATA/video/< not found!",
                    LogCat.Loading);
                return;
            }

            VideoFilePathsBik = Directory.EnumerateFiles(videoFileFolder)
                .Where(i => Path.GetExtension(i).EqualsIgnoreCase(".bik")).ToList();

            if (VideoFilePathsBik.IsEmpty())
            {
                Logger.LogWarning($"No Bink videos found in the video folder at >{videoFileFolder}<.", LogCat.Loading);
            }
        }

        /// <summary>
        /// Path of the video a script asks for (name with or without extension), null if missing.
        /// </summary>
        public string FindVideo(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            return VideoFilePathsBik
                .FirstOrDefault(i => Path.GetFileNameWithoutExtension(i).EqualsIgnoreCase(name));
        }
    }
}
