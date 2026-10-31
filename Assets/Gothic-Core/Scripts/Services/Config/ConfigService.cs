using System;
using System.IO;
using System.Linq;
using Gothic.Core.Domain.Config;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Config;
using MyBox;
using ZenKit;

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

        /// <summary>
        /// DeveloperConfig PreSelectMod + ModPath.
        /// </summary>
        public string EffectiveModPath =>
            Dev.PreSelectMod && !Dev.ModPath.IsNullOrEmpty() ? Dev.ModPath : null;

        /// <summary>
        /// The game to start without the game selection (DeveloperConfig PreselectGameVersion + GameVersion).
        /// </summary>
        public bool TryGetPreselectedGameVersion(out GameVersion version)
        {
            version = Dev.GameVersion;
            return Dev.PreselectGameVersion;
        }

        public string EffectiveModIni =>
            Dev.PreSelectMod && !Dev.ModIni.IsNullOrEmpty() ? Dev.ModIni : null;

        public string EffectiveModIniFileName => EffectiveModIni.IsNullOrEmpty() ? "GothicGame.ini" : EffectiveModIni;

        /// <summary>
        /// Whether a mod is active right now, i.e. ModPath (mod in its own game root) or ModIni (mod installed
        /// into the Gothic folder, selected by its ini) resolved to something. This is the single source of
        /// truth for "is a mod active" — use it instead of Dev.PreSelectMod directly, which only means
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
            var gothicIniPath = Path.Combine(baseRootPath, "system/Gothic.ini");

            var gothicModIniPath = Path.Combine(rootPath, "system", EffectiveModIniFileName);

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
