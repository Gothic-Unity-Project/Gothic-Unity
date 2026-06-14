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
            var rootPath = version == GameVersion.Gothic1 ? Root.Gothic1Path : Root.Gothic2Path;
            var gothicIniPath = Path.Combine(rootPath, "system/Gothic.ini");
            
#if UNITY_EDITOR
            var effectiveModPath = Dev.EnableMod && !Dev.ModPath.IsNullOrEmpty() ? Dev.ModPath : null;
            var effectiveModIni = Dev.EnableMod && !Dev.ModIni.IsNullOrEmpty() ? Dev.ModIni : null;
#else
            var effectiveModPath = Root.ModPath;
            var effectiveModIni = Root.ModIni;
#endif
            var modIniFileName = effectiveModIni.IsNullOrEmpty() ? "GothicGame.ini" : effectiveModIni;
            var modSystemPath = !effectiveModPath.IsNullOrEmpty()
                ? Path.Combine(effectiveModPath, "system", modIniFileName)
                : null;
            var gothicModIniPath = modSystemPath != null && File.Exists(modSystemPath)
                ? modSystemPath
                : Path.Combine(rootPath, "system", modIniFileName);

            Gothic = new GothicIniConfig(IniLoader.LoadFile(gothicIniPath), gothicIniPath);
            GothicMod = new GothicModIniConfig(IniLoader.LoadFile(gothicModIniPath), gothicModIniPath);
            
            if (!GothicMod.IsLoaded)
                throw new ArgumentException($"GothicMod Ini file not found: {gothicModIniPath}");
            
            GlobalEventDispatcher.GothicInisInitialized.Invoke();
        }

        public bool CheckIfGothicInstallationExists(GameVersion version)
        {
            var gothicRootPath = version == GameVersion.Gothic1 ? Root.Gothic1Path : Root.Gothic2Path;

            var gothicDataPath = $"{gothicRootPath}/Data";
            var gothicWorkPath = $"{gothicRootPath}/_work";

            return Directory.Exists(gothicWorkPath) && Directory.Exists(gothicDataPath);
        }
    }
}
