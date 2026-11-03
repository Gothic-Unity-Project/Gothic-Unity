namespace Gothic.Core.Models.SaveGames
{
    public class UnitySaveHeader
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public string GothicUnityVersion;
        public string Game;

        /// <summary>
        /// Ini of the active mod (GothicGame.ini for the base game).
        /// </summary>
        public string ModIni;

        /// <summary>
        /// Hash over the compiled scripts' symbol names. A different value means other scripts (mod or update).
        /// </summary>
        public string ScriptSignature;

        public string SavedAtUtc;
    }
}
