using ZenKit;

namespace Gothic.Core.Models.Config
{
    /// <summary>
    /// One startable entry of the game selection: an installed game with one of the game inis in its system/ folder.
    /// GothicGame.ini = the vanilla game, any other game ini = a mod installed into that game.
    /// </summary>
    public class GameProfile
    {
        public string Name;
        public GameVersion Version;
        public string IniFileName;
    }
}
