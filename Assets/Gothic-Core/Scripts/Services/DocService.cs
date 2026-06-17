using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Models.Doc;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services
{
    public class DocService
    {
        private readonly Dictionary<int, DocModel> _docs = new();
        private int _nextId = 1;

        // Set by VR/Flat adapter before calling on_state[0] so Doc_Show knows which GO to attach to.
        public GameObject PendingItemGo;

        public int CreateDoc()
        {
            var doc = new DocModel { Id = _nextId++ };
            _docs[doc.Id] = doc;
            Logger.Log($"[DocService] Doc_Create → id={doc.Id}", LogCat.Npc);
            return doc.Id;
        }

        public int CreateMap()
        {
            var doc = new DocModel { Id = _nextId++, IsMap = true };
            _docs[doc.Id] = doc;
            Logger.Log($"[DocService] Doc_CreateMap → id={doc.Id}", LogCat.Npc);
            return doc.Id;
        }

        public void SetPages(int id, int count)
        {
            var doc = Get(id);
            if (doc == null) return;
            while (doc.Pages.Count < count)
                doc.Pages.Add(new DocPage());
            while (doc.Pages.Count > count)
                doc.Pages.RemoveAt(doc.Pages.Count - 1);
        }

        public void SetPage(int id, int pageIndex, string texture, int flags)
        {
            var page = GetPage(id, pageIndex);
            if (page == null) return;
            page.Texture = texture;
            page.Flags = flags;
        }

        public void SetMargins(int id, int pageIndex, int left, int top, int right, int bottom, int type)
        {
            if (pageIndex == -1)
            {
                foreach (var p in GetAll(id)) ApplyMargins(p, left, top, right, bottom);
            }
            else
            {
                var page = GetPage(id, pageIndex);
                if (page != null) ApplyMargins(page, left, top, right, bottom);
            }
        }

        public void SetFont(int id, int pageIndex, string font)
        {
            if (pageIndex == -1)
            {
                foreach (var p in GetAll(id)) p.Font = font;
            }
            else
            {
                var page = GetPage(id, pageIndex);
                if (page != null) page.Font = font;
            }
        }

        public void PrintLine(int id, int pageIndex, string text)
        {
            var page = GetPage(id, pageIndex);
            page?.Lines.Add(text ?? "");
        }

        public void PrintLines(int id, int pageIndex, string text)
        {
            // Gothic word-wraps; we store as one paragraph and let TMP handle wrapping at render time.
            var page = GetPage(id, pageIndex);
            page?.Lines.Add(text ?? "");
        }

        public void ShowDoc(int id)
        {
            var doc = Get(id);
            if (doc == null)
            {
                Logger.LogWarning($"[DocService] Doc_Show: doc id={id} not found", LogCat.Npc);
                return;
            }
            Logger.Log($"[DocService] Doc_Show id={id}, pages={doc.Pages.Count}, itemGo={PendingItemGo?.name}", LogCat.Npc);
            GlobalEventDispatcher.DocShow.Invoke(doc, PendingItemGo);
            PendingItemGo = null;
        }

        private DocModel Get(int id) => _docs.TryGetValue(id, out var d) ? d : null;

        private DocPage GetPage(int id, int pageIndex)
        {
            var doc = Get(id);
            if (doc == null || pageIndex < 0 || pageIndex >= doc.Pages.Count) return null;
            return doc.Pages[pageIndex];
        }

        private IEnumerable<DocPage> GetAll(int id)
        {
            var doc = Get(id);
            return doc?.Pages ?? new List<DocPage>();
        }

        private static void ApplyMargins(DocPage page, int left, int top, int right, int bottom)
        {
            page.MarginLeft = left;
            page.MarginTop = top;
            page.MarginRight = right;
            page.MarginBottom = bottom;
        }
    }
}
