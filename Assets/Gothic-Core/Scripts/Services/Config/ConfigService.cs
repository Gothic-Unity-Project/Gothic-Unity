using System;
using System.IO;
using Gothic.Core.Domain.Config;
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

        public string EffectiveModPath
        {
            get
            {
#if UNITY_EDITOR
                return Dev.EnableMod && !Dev.ModPath.IsNullOrEmpty() ? Dev.ModPath : null;
#else
                return Root.ModPath;
#endif
            }
        }

        public string EffectiveModIni
        {
            get
            {
#if UNITY_EDITOR
                return Dev.EnableMod && !Dev.ModIni.IsNullOrEmpty() ? Dev.ModIni : null;
#else
                return Root.ModIni;
#endif
            }
        }

        /// <summary>
        /// EffectiveModIni with the same "GothicGame.ini" fallback LoadGothicInis applies — a mod
        /// (e.g. New Balance) can be active with no ModIni set at all, relying purely on the default.
        /// </summary>
        public string EffectiveModIniFileName => EffectiveModIni.IsNullOrEmpty() ? "GothicGame.ini" : EffectiveModIni;

        /// <summary>
        /// Whether a mod is active right now, i.e. whether ModPath resolved to something. This is the
        /// single source of truth for "is a mod active" — use it instead of Dev.EnableMod directly,
        /// which only means "use the Editor's ModPath/ModIni override" and is meaningless in a build.
        /// </summary>
        public bool IsModActive => !EffectiveModPath.IsNullOrEmpty();

        /// <summary>
        /// Folder-safe suffix identifying the active mod, so StaticCacheService keeps a separate cache
        /// per (GameVersion, mod) instead of one shared per GameVersion — switching between vanilla and
        /// several mods (or between mods) would otherwise stomp on each other's cached VOB bounds/texture
        /// arrays/world chunks, forcing a full recreate every time. Naming uses the resolved ini file,
        /// e.g. "_mod_GothicGame" or "_mod_DM_E". Empty when running vanilla.
        /// </summary>
        public string ModCacheSuffix => IsModActive
            ? $"_mod_{Path.GetFileNameWithoutExtension(EffectiveModIniFileName)}"
            : string.Empty;

        /// <summary>
        /// Some mod music compositions embed a native dmusic segue we can't intercept (see
        /// MusicDomain.PCMReaderCallback) — turning music off entirely is the only workaround.
        /// Standalone builds read this from GameSettings.json/.dev.json so one build can be
        /// repointed at a different mod (and its music requirement) without a recompile; the
        /// Editor uses the DeveloperConfig Inspector toggle instead for quick iteration.
        /// </summary>
        public bool EffectiveEnableMusic
        {
            get
            {
#if UNITY_EDITOR
                return Dev.EnableMusic;
#else
                return Root.EnableMusic;
#endif
            }
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
            return Directory.Exists($"{rootPath}/_work") && Directory.Exists($"{rootPath}/Data");
        }
    }
}
