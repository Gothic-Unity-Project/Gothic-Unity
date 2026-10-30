using System.Collections.Generic;

namespace Gothic.Core.Models.Doc
{
    /// <summary>
    /// A book, letter or map the scripts build page by page (Doc_Create/Doc_CreateMap) and then show (Doc_Show).
    /// </summary>
    public class DocModel
    {
        public int Id;
        public bool IsMap;
        public readonly List<DocPage> Pages = new();
        // Page settings given for all pages (page -1) before the pages exist.
        public string DefaultFont;
        public int[] DefaultMargins;

        // Doc_SetLevel: the world the map shows, e.g. "WORLD.ZEN". The hero is marked on it only there.
        public string Level;
        // Doc_SetLevelCoords (G2): world coordinates (cm) of the map's edges.
        public bool HasLevelCoords;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
