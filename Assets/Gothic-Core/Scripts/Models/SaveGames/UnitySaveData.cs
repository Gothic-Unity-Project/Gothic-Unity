using System.Collections.Generic;

namespace Gothic.Core.Models.SaveGames
{
    /// <summary>
    /// Content of UNITYSAVE.json next to the Gothic save files. Holds state which Gothic's save format doesn't know.
    /// </summary>
    public class UnitySaveData
    {
        public UnitySaveHeader Header = new();

        /// <summary>
        /// Key: section key like "core.xyz" or "vr.xyz". Sections without a registered owner (e.g. written by a newer
        /// build or a VR mod) are kept when saving again.
        /// </summary>
        public Dictionary<string, UnitySaveSection> Sections = new();
    }
}
