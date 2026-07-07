using System;
using System.Collections.Generic;

namespace Gothic.Core.Models.Config
{
    public class GothicModIniConfig
    {
        private readonly Dictionary<string, string> _config;

        public readonly string IniFilePath;
        public readonly bool IsLoaded;

        public string Player => _config.GetValueOrDefault("player", "PC_HERO");

        // Mod inis may prefix the world with its subfolder (G2 Renovation: "NewWorld\NEWWORLD.ZEN").
        // Our VFS lookup is flat by file name, so only the name part is meaningful here.
        public string World => System.IO.Path.GetFileName(_config.GetValueOrDefault("world", "World.zen"));

        // [FILES] vdf=... lists the exact .mod/.vdf archives this ini wants mounted. Multi-language mod
        // packages (e.g. Dolina Zombie) ship every language's archives side by side in Data/, relying on
        // the chosen ini to pick just one set — without this, all languages get merged together.
        public IReadOnlyList<string> Vdfs => _config.GetValueOrDefault("vdf", "")
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);



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
