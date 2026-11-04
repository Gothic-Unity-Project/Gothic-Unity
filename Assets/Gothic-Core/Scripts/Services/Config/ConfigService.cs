using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Domain.Config;
using Gothic.Core.Logging;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Config;
using MyBox;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Config
{
    /// <summary>
    /// Combines three sources of configuration:
    /// 1. Gothic.ini and GothicGame.ini from Gothic installation directory containing original Gothic settings
    /// 2. GameSettings.json from Gothic-Unity/StreamingAssets path for root configuration (e.g. log level)
    /// 3. DeveloperConfig ScriptableObject for developer settings
    /// </summary>
    public class ConfigService
    {
        public JsonRootConfig Root { get; private set; }
        public DeveloperConfig Dev { get; private set; }
        public GothicIniConfig Gothic { get; private set; }
        public GothicModIniConfig GothicMod { get; private set; }

        // The vanilla game's own ini; every other game ini in system/ is a mod installed into the game.
        private const string _vanillaGameIni = "GothicGame.ini";

        /// <summary>
        /// The profile picked or auto-selected in GameVersionScene - its game ini wins over DeveloperConfig's mod.
        /// </summary>
        public GameProfile SelectedGameProfile { get; private set; }

        /// <summary>
        /// A selected profile's mod folder (GameSettings.json ModFolders; none for a game folder's ini), otherwise
        /// DeveloperConfig PreSelectMod + ModPath.
        /// </summary>
        public string EffectiveModPath => SelectedGameProfile != null
            ? SelectedGameProfile.ModPath
            : Dev.PreSelectMod && !Dev.ModPath.IsNullOrEmpty() ? Dev.ModPath : null;

        /// <summary>
        /// The game to start without the game selection (DeveloperConfig PreselectGameVersion + GameVersion).
        /// </summary>
        public bool TryGetPreselectedGameVersion(out GameVersion version)
        {
            version = Dev.GameVersion;
            return Dev.PreselectGameVersion;
        }

        /// <summary>
        /// Selected profile's ini (null for the vanilla GothicGame.ini), otherwise DeveloperConfig PreSelectMod + ModIni.
        /// </summary>
        public string EffectiveModIni
        {
            get
            {
                if (SelectedGameProfile != null)
                    return SelectedGameProfile.IniFileName.EqualsIgnoreCase(_vanillaGameIni)
                        ? null
                        : SelectedGameProfile.IniFileName;

                return Dev.PreSelectMod && !Dev.ModIni.IsNullOrEmpty() ? Dev.ModIni : null;
            }
        }

        /// <summary>
        /// Everything startable: each installed game (GameSettings.json Gothic1Path/Gothic2Path) with each game ini
        /// of its system/ folder (ModIniScanner) - vanilla GothicGame.ini and the mods installed into the game -, then
        /// the game inis of each mod folder of that game (GameSettings.json ModFolders).
        /// </summary>
        public List<GameProfile> FindGameProfiles()
        {
            var profiles = new List<GameProfile>();
            foreach (var version in new[] { GameVersion.Gothic1, GameVersion.Gothic2 })
            {
                if (!CheckIfGothicInstallationExists(version))
                    continue;

                var rootPath = version == GameVersion.Gothic1 ? Root.Gothic1Path : Root.Gothic2Path;
                var inis = ModIniScanner.FindGameInis(rootPath);

                // No readable game ini (e.g. a GothicGame.ini without [INFO] title=) - the game still starts as before.
                if (inis.Count == 0)
                {
                    Logger.LogWarning($"No game ini found in {rootPath}/system - offering {version} with " +
                                      $"{_vanillaGameIni}.", LogCat.Loading);
                    inis.Add(new ModIniScanner.ModIni { FileName = _vanillaGameIni, Title = version.ToString() });
                }

                // Vanilla game on top, then its mods by title (the scanner returns them in file system order).
                var sortedInis = inis
                    .OrderByDescending(i => i.FileName.EqualsIgnoreCase(_vanillaGameIni))
                    .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase);

                foreach (var ini in sortedInis)
                {
                    var isVanilla = ini.FileName.EqualsIgnoreCase(_vanillaGameIni);
                    profiles.Add(new GameProfile
                    {
                        Name = isVanilla ? ini.Title : $"{ini.Title} ({ini.FileName})",
                        Version = version,
                        IniFileName = ini.FileName
                    });
                }

                AddModFolderProfiles(version, profiles);
            }

            return profiles;
        }

        /// <summary>
        /// GameSettings.json ModFolders of this game: each game ini of a mod folder's system/ folder, by title.
        /// </summary>
        private void AddModFolderProfiles(GameVersion version, List<GameProfile> profiles)
        {
            if (Root.ModFolders == null)
                return;

            foreach (var folder in Root.ModFolders)
            {
                if (folder == null || !Enum.TryParse<GameVersion>(folder.GameVersion, true, out var folderVersion) ||
                    folderVersion != version)
                    continue;

                var inis = ModIniScanner.FindGameInis(folder.Path);
                if (inis.Count == 0)
                {
                    Logger.LogWarning($"ModFolders: no game ini in {folder.Path}/system - skipped.", LogCat.Loading);
                    continue;
                }

                foreach (var ini in inis.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase))
                {
                    profiles.Add(new GameProfile
                    {
                        Name = $"{ini.Title} ({ini.FileName})",
                        Version = version,
                        IniFileName = ini.FileName,
                        ModPath = folder.Path
                    });
                }
            }
        }

        public void SelectGameProfile(GameProfile profile)
        {
            SelectedGameProfile = profile;
            Logger.Log($"Game profile selected: >{profile.Name}< ({profile.Version}, ini: {profile.IniFileName}" +
                       $"{(profile.ModPath.IsNullOrEmpty() ? "" : $", mod folder: {profile.ModPath}")})",
                LogCat.Loading);
        }

        /// <summary>
        /// EffectiveModIni with the same "GothicGame.ini" fallback LoadGothicInis applies — a mod
        /// (e.g. New Balance) can be active with no ModIni set at all, relying purely on the default.
        /// </summary>
        public string EffectiveModIniFileName => EffectiveModIni.IsNullOrEmpty() ? _vanillaGameIni : EffectiveModIni;

        /// <summary>
        /// Whether a mod is active right now, i.e. ModPath (mod in its own game root) or ModIni (mod installed
        /// into the Gothic folder, selected by its ini) resolved to something. This is the single source of
        /// truth for "is a mod active" - use it instead of Dev.PreSelectMod directly, which only means
        /// "use DeveloperConfig's ModPath/ModIni" and ignores any other mod source.
        /// </summary>
        public bool IsModActive => !EffectiveModPath.IsNullOrEmpty() || !EffectiveModIni.IsNullOrEmpty();

        /// <summary>
        /// Folder-safe suffix identifying the active mod, so StaticCacheService keeps a separate cache
        /// per (GameVersion, mod) instead of one shared per GameVersion — switching between vanilla and
        /// several mods (or between mods) would otherwise stomp on each other's cached VOB bounds/texture
        /// arrays/world chunks, forcing a full recreate every time. Naming uses the mod folder (only if the mod
        /// has its own ModPath) and the resolved ini file (several mods use the default GothicGame.ini), e.g.
        /// "_mod_MroczneTajemnice_GothicGame" or "_mod_DM_E" for a mod inside the Gothic folder.
        /// Empty when running vanilla.
        /// </summary>
        public string ModCacheSuffix
        {
            get
            {
                if (!IsModActive)
                    return string.Empty;

                var iniName = ToFolderSafeName(Path.GetFileNameWithoutExtension(EffectiveModIniFileName));
                if (EffectiveModPath.IsNullOrEmpty())
                    return $"_mod_{iniName}";

                var folderName = ToFolderSafeName(Path.GetFileName(EffectiveModPath.TrimEnd('/', '\\')));
                return $"_mod_{folderName}_{iniName}";
            }
        }

        private static string ToFolderSafeName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(name.Where(c => !char.IsWhiteSpace(c) && !invalidChars.Contains(c)).ToArray());
        }

        /// <summary>
        /// First one to load.
        /// Root, as it contains only a few Gothic specific bootstrap data like
        /// installation directory of Gothic1/2 and LogLevel.
        /// </summary>
        public void LoadRootJson()
        {
            Root = JsonRootLoader.Load();
        }

        /// <summary>
        /// Config provided from caller (basically GameManager or LabManager).
        /// </summary>
        public void SetDeveloperConfig(DeveloperConfig config)
        {
            // We simply reference the ScriptableObject from GameManager component.
            Dev = config;
        }

        /// <summary>
        /// Last one to be loaded. Whenever GameVersion is set already.
        /// </summary>
        public void LoadGothicInis(GameVersion version)
        {
            var baseRootPath = version == GameVersion.Gothic1 ? Root.Gothic1Path : Root.Gothic2Path;
            var rootPath = EffectiveModPath ?? baseRootPath;
            // In any case (Linux: an installation may have System/ or GOTHIC.INI). A missing one keeps its expected
            // path for the error.
            var gothicIniPath = PathExtension.FindIgnoringCase(baseRootPath, "system/Gothic.ini") ??
                                Path.Combine(baseRootPath, "system/Gothic.ini");

            var gothicModIniPath = PathExtension.FindIgnoringCase(rootPath, "system", EffectiveModIniFileName) ??
                                   Path.Combine(rootPath, "system", EffectiveModIniFileName);

            Gothic = new GothicIniConfig(IniLoader.LoadFile(gothicIniPath), gothicIniPath);
            GothicMod = new GothicModIniConfig(IniLoader.LoadFile(gothicModIniPath), gothicModIniPath);
            
            if (!GothicMod.IsLoaded)
                throw new ArgumentException($"GothicMod Ini file not found: {gothicModIniPath}");
            
            GlobalEventDispatcher.GothicInisInitialized.Invoke();
        }

        public bool CheckIfGothicInstallationExists(GameVersion version)
        {
            var rootPath = version == GameVersion.Gothic1 ? Root.Gothic1Path : Root.Gothic2Path;
            return PathExtension.FindIgnoringCase(rootPath, "_work") != null &&
                   PathExtension.FindIgnoringCase(rootPath, "Data") != null;
        }
    }
}
