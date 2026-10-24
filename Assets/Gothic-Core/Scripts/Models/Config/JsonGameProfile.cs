using System;

namespace Gothic.Core.Models.Config
{
    /// <summary>
    /// One entry of GameSettings.json "GameProfiles": a game (Gothic1/Gothic2) with an optional mod.
    /// No ModPath = vanilla game with its normal GothicGame.ini.
    /// </summary>
    [Serializable]
    public class JsonGameProfile
    {
        public string Name;
        public string GameVersion;
        public string ModPath;
        public string ModIni;
    }
}
