using ZenKit;

namespace Gothic.Core.Models.Config
{
    /// <summary>
    /// One startable entry of the game selection: an installed game with one of the game inis in its system/ folder.
    /// GothicGame.ini = the vanilla game, any other game ini = a mod installed into that game. Or one of a mod
    /// folder's (ModPath) game inis.
    /// </summary>
    public class GameProfile
    {
        public string Name;
        public GameVersion Version;
        public string IniFileName;
        // A mod in its own game copy (GameSettings.json ModFolders), null for an ini of the game folder.
        public string ModPath;
    }
}
