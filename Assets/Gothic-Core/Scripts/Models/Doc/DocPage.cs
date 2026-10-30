using System.Collections.Generic;

namespace Gothic.Core.Models.Doc
{
    /// <summary>
    /// One page of a document (Doc_SetPage, Doc_SetFont, Doc_SetMargins, Doc_PrintLine(s)).
    /// </summary>
    public class DocPage
    {
        // Background texture, e.g. "Letters.TGA" or the map.
        public string Texture;
        public string Font;
        // Margins in pixels of the page texture.
        public int MarginLeft;
        public int MarginTop;
        public int MarginRight;
        public int MarginBottom;
        // Doc_PrintLine adds a line, Doc_PrintLines a paragraph that wraps.
        public readonly List<string> Lines = new();
    }
}
