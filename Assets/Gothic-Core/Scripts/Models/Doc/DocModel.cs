using System.Collections.Generic;

namespace Gothic.Core.Models.Doc
{
    public class DocPage
    {
        public string Texture;
        public int Flags;
        public string Font;
        public int MarginLeft;
        public int MarginTop;
        public int MarginRight;
        public int MarginBottom;
        public List<string> Lines = new();
    }

    public class DocModel
    {
        public int Id;
        public bool IsMap;
        public List<DocPage> Pages = new();

        /// <summary>
        /// Doc_SetLevel: the world the map shows (e.g. WORLD.ZEN) - the hero is marked on it only there.
        /// </summary>
        public string Level;

        /// <summary>
        /// Doc_SetLevelCoords: world coordinates (cm) of the map edges. G1 maps have none - the world's bounds.
        /// </summary>
        public bool HasLevelCoords;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
