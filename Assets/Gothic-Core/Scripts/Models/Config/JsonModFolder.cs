using System;

namespace Gothic.Core.Models.Config
{
    /// <summary>
    /// GameSettings.json ModFolders: a mod installed in its own copy of a game (e.g. Mroczne Tajemnice next to
    /// Gothic), not into the game folder. Its system/ folder's game inis are offered like the game's own.
    /// </summary>
    [Serializable]
    public class JsonModFolder
    {
        // Gothic1 or Gothic2.
        public string GameVersion;
        public string Path;
    }
}
