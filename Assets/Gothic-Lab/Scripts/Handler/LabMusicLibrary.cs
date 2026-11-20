using System.Collections.Generic;
using System.Linq;
using DirectMusic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Caches;
using JetBrains.Annotations;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Lab.Handler
{
    /// <summary>
    /// The music themes (C_MUSICTHEME) of one game and their segments. Built once at Lab boot and kept as long as the
    /// Lab scene lives - the theme instances need their VM.
    /// </summary>
    public class LabMusicLibrary
    {
        public readonly GameVersion Version;
        public readonly IReadOnlyList<(string Name, MusicThemeInstance Theme)> Themes;

        // The running game's injected service, or a second instance for the other game.
        // Lab only: music only - its injected context services belong to the running game.
        private readonly ResourceCacheService _resourceCacheService;
        // Owns the theme instances - not used otherwise.
        private readonly DaedalusVm _vm;

        /// <summary>
        /// The running game's themes from its already initialized ResourceCacheService.
        /// </summary>
        public LabMusicLibrary(GameVersion version, ResourceCacheService runningGameResources)
        {
            Version = version;
            _resourceCacheService = runningGameResources;
            (_vm, Themes) = LoadThemes();
        }

        /// <summary>
        /// The other game's themes: mounts its installation (vanilla, no mods) into a ResourceCacheService of its own.
        /// </summary>
        public LabMusicLibrary(GameVersion version, string gameRootPath)
        {
            Version = version;
            _resourceCacheService = new ResourceCacheService().Inject();
            _resourceCacheService.Init(gameRootPath);
            (_vm, Themes) = LoadThemes();
        }

        [CanBeNull]
        public Segment TryGetSegment(MusicThemeInstance theme)
        {
            return _resourceCacheService.TryGetSegment(theme.File);
        }

        private (DaedalusVm, IReadOnlyList<(string, MusicThemeInstance)>) LoadThemes()
        {
            var vm = _resourceCacheService.TryGetDaedalusVm("MUSIC");
            if (vm == null)
            {
                Logger.LogWarning($"[Music] Lab: no MUSIC.DAT for {Version} - its themes are left out.", LogCat.Audio);
                return (null, new List<(string, MusicThemeInstance)>());
            }

            var themes = vm.GetInstanceSymbols("C_MUSICTHEME")
                .Select(s => (s.Name, vm.InitInstance<MusicThemeInstance>(s)))
                .ToList();

            Logger.Log($"[Music] Lab: {themes.Count} themes of {Version} loaded.", LogCat.Audio);
            return (vm, themes);
        }
    }
}
