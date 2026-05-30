using System.Linq;
using GUZ.Core.Manager;
using Reflex.Attributes;
using TMPro;

namespace GUZ.Lab.Handler
{
    public class LabMusicHandler : AbstractLabHandler
    {
        public TMP_Dropdown FileSelector;

        [Inject] private readonly AudioService _audioService;

        public override void Bootstrap()
        {
            var vm = ResourceCacheService.TryGetDaedalusVm("MUSIC");

            var musicInstances = vm.GetInstanceSymbols("C_MUSICTHEME")
                .Select(s => s.Name)
                .ToList();

            FileSelector.options = musicInstances.Select(i => new TMP_Dropdown.OptionData(i)).ToList();
        }

        public void MusicPlayClick()
        {
            _audioService.PlayMusic(FileSelector.options[FileSelector.value].text);
        }
    }
}
