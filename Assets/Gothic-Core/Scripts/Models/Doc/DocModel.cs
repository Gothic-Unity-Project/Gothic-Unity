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
    }
}
