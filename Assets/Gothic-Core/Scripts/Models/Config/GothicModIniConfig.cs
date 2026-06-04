using System.Collections.Generic;

namespace Gothic.Core.Models.Config
{
    public class GothicModIniConfig
    {
        private readonly Dictionary<string, string> _config;

        public readonly string IniFilePath;
        public readonly bool IsLoaded;

        public string Player => _config.GetValueOrDefault("player", "PC_HERO");
        public string World => _config.GetValueOrDefault("world", "World.zen");
        


        public GothicModIniConfig(Dictionary<string, string> config, string iniFilePath)
        {
            _config = config;
            IniFilePath = iniFilePath;

            // Safety check if the mod file selected doesn't exist.
            if (_config != null)
                IsLoaded = true;
        }
    }
}
