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
        /// The item instance whose use shows the document (e.g. ItWr_Book_Circle_01), null for a mob's page.
        /// </summary>
        public string SourceName;

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

        /// <summary>
        /// Which document this is, the same in every session (Id is only a runtime counter): the item instance, or
        /// for a mob's page a hash of its textures and text. E.g. for "read N different documents".
        /// </summary>
        public string Identity => SourceName ?? $"doc:{GetContentHash():x8}";

        /// <summary>
        /// FNV-1a over the pages' textures and lines (string.GetHashCode() changes between sessions).
        /// </summary>
        private uint GetContentHash()
        {
            var hash = 2166136261u;
            foreach (var page in Pages)
            {
                hash = AddToHash(hash, page.Texture);
                foreach (var line in page.Lines)
                    hash = AddToHash(hash, line);
            }

            return hash;
        }

        private static uint AddToHash(uint hash, string text)
        {
            foreach (var c in text ?? string.Empty)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            hash ^= '|';
            return hash * 16777619u;
        }
    }
}
