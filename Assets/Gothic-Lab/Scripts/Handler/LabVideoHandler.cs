using System.Linq;
using GUZ.Core;
using GUZ.Core.Services;
using GUZ.Core.Services.Player;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

namespace GUZ.Lab.Handler
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
