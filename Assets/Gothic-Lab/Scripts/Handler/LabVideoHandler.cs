using System.Linq;
using Gothic.Core.Services.Player;
using Gothic.Core;
using Gothic.Core.Services;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

namespace Gothic.Lab.Handler
{
    public class LabVideoHandler : AbstractLabHandler
    {
        [SerializeField] private TMP_Dropdown _fileSelector;
        [SerializeField] private VideoPlayer _videoPlayer;
        
        
        [Inject] private readonly VideoService _videoService;


        public override void Bootstrap()
        {
            _fileSelector.options = _videoService.VideoFileNamesMp4.Select(i => new TMP_Dropdown.OptionData(i)).ToList();
        }

        public void VideoPlayClick()
        {
            _videoPlayer.url = _videoService.VideoFilePathsMp4[_fileSelector.value];
            _videoPlayer.Play();
        }
    }
}
