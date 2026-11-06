using System.Collections.Generic;

namespace Gothic.Core.Models.Config
{
    public class JsonRootConfig
    {
        public string Gothic1Path;
        public string Gothic2Path;
        // Mods in their own game copies, offered in the game selection next to the games' own inis.
        public List<JsonModFolder> ModFolders;
        public string LogLevel;
        public string LogCategories;
    }
}
