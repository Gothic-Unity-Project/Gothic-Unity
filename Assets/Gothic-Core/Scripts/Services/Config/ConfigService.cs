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

            var modIniFileName = EffectiveModIni.IsNullOrEmpty() ? "GothicGame.ini" : EffectiveModIni;
            var gothicModIniPath = Path.Combine(rootPath, "system", modIniFileName);

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
