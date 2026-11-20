using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Lab.Handler
{
    /// <summary>
    /// Plays the music themes of Gothic 1 and Gothic 2 from one dropdown, with the transition the music scripts give
    /// each theme. Favorites are buttons for quick switching; they live as long as the Lab scene and aren't saved.
    /// </summary>
    public class LabMusicHandler : AbstractLabHandler
    {
        public TMP_Dropdown FileSelector;
        [Tooltip("Inactive template (Button with a TMP_Text child) cloned for each favorite.")]
        public Button FavoriteTemplate;
        [Tooltip("Parent of the favorite buttons (content of the favorites list).")]
        public Transform FavoritesContent;

        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly ConfigService _configService;

        // Kept for the Lab's lifetime: the theme instances need their VM, the segments their ResourceCacheService.
        private readonly List<LabMusicLibrary> _libraries = new();
        private readonly List<(LabMusicLibrary Library, MusicThemeInstance Theme, string Label)> _entries = new();
        private readonly HashSet<int> _favoriteIndices = new();
        private LabMusicLibrary _currentLibrary;
        private string _currentLabel;

        public override void Bootstrap()
        {
            FileSelector.ClearOptions();

            if (_configService.GothicMod.UnitySkipMusicPlayback)
            {
                Logger.LogWarning("[Music] Lab: music playback is skipped by the game ini - no themes listed.",
                    LogCat.Audio);
                return;
            }

            var runningVersion = _configService.Dev.GameVersion;
            _libraries.Add(new LabMusicLibrary(runningVersion, ResourceCacheService));

            var otherVersion = runningVersion == GameVersion.Gothic1 ? GameVersion.Gothic2 : GameVersion.Gothic1;
            if (_configService.CheckIfGothicInstallationExists(otherVersion))
                _libraries.Add(new LabMusicLibrary(otherVersion, _configService.GetGameRootPath(otherVersion)));
            else
                Logger.LogWarning($"[Music] Lab: no {otherVersion} installation found - its themes are left out.",
                    LogCat.Audio);

            foreach (var library in _libraries.OrderBy(i => i.Version))
            {
                foreach (var (name, theme) in library.Themes)
                    _entries.Add((library, theme, $"{ToPrefix(library.Version)} · {name}"));
            }

            FileSelector.options = _entries.Select(i => new TMP_Dropdown.OptionData(i.Label)).ToList();
        }

        public void MusicPlayClick()
        {
            if (_entries.Count == 0)
                return;

            PlayEntry(FileSelector.value);
        }

        public void MusicFavoriteClick()
        {
            if (_entries.Count == 0)
                return;

            var index = FileSelector.value;
            if (!_favoriteIndices.Add(index))
                return;

            var favorite = Instantiate(FavoriteTemplate, FavoritesContent);
            favorite.gameObject.SetActive(true);
            favorite.GetComponentInChildren<TMP_Text>(true).text = _entries[index].Label;
            favorite.onClick.AddListener(() => PlayEntry(index));
        }

        private void PlayEntry(int index)
        {
            var (library, theme, label) = _entries[index];
            var segment = library.TryGetSegment(theme);
            if (segment == null)
            {
                Logger.LogWarning($"[Music] Lab: {label} can't be played.", LogCat.Audio);
                return;
            }

            if (label != _currentLabel)
            {
                var isCrossGame = _currentLibrary != null && _currentLibrary != library;
                Logger.Log($"[Music] Lab: {_currentLabel ?? "-"} -> {label}" +
                           (isCrossGame ? " (switch between the games)" : ""), LogCat.Audio);
            }

            _audioService.PlayMusic(theme, segment);
            _currentLibrary = library;
            _currentLabel = label;
        }

        private static string ToPrefix(GameVersion version)
        {
            return version == GameVersion.Gothic1 ? "G1" : "G2";
        }
    }
}
