using System.Collections.Generic;
using System.IO;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Doc;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Player
{
    /// <summary>
    /// Books, letters and maps: the Doc_* externals a document's use function calls build it page by page, Doc_Show
    /// hands it to the platform's viewer (GlobalEventDispatcher.DocShown). Like the engine, a page setting for page -1
    /// applies to all pages.
    /// </summary>
    public class DocService
    {
        [Inject] private readonly SaveGameService _saveGameService;

        private readonly Dictionary<int, DocModel> _docs = new();
        private int _nextId = 1;

        public int Create(bool isMap)
        {
            var doc = new DocModel { Id = _nextId++, IsMap = isMap };
            _docs[doc.Id] = doc;
            return doc.Id;
        }

        public void SetPages(int id, int count)
        {
            if (!_docs.TryGetValue(id, out var doc))
                return;

            while (doc.Pages.Count < count)
            {
                var page = new DocPage { Font = doc.DefaultFont };
                if (doc.DefaultMargins != null)
                    SetMargins(page, doc.DefaultMargins);
                doc.Pages.Add(page);
            }
            while (doc.Pages.Count > count)
                doc.Pages.RemoveAt(doc.Pages.Count - 1);
        }

        public void SetPage(int id, int pageIndex, string texture)
        {
            foreach (var page in GetPages(id, pageIndex))
                page.Texture = texture;
        }

        public void SetFont(int id, int pageIndex, string font)
        {
            if (pageIndex == -1 && _docs.TryGetValue(id, out var doc))
                doc.DefaultFont = font;
            foreach (var page in GetPages(id, pageIndex))
                page.Font = font;
        }

        public void SetMargins(int id, int pageIndex, int left, int top, int right, int bottom)
        {
            var margins = new[] { left, top, right, bottom };
            if (pageIndex == -1 && _docs.TryGetValue(id, out var doc))
                doc.DefaultMargins = margins;
            foreach (var page in GetPages(id, pageIndex))
                SetMargins(page, margins);
        }

        public void PrintLine(int id, int pageIndex, string text)
        {
            foreach (var page in GetPages(id, pageIndex))
                page.Lines.Add(text ?? string.Empty);
        }

        public void SetLevel(int id, string level)
        {
            if (_docs.TryGetValue(id, out var doc))
                doc.Level = level;
        }

        public void SetLevelCoords(int id, int left, int top, int right, int bottom)
        {
            if (!_docs.TryGetValue(id, out var doc))
                return;

            doc.HasLevelCoords = true;
            (doc.Left, doc.Top, doc.Right, doc.Bottom) = (left, top, right, bottom);
        }

        public void Show(int id)
        {
            if (!_docs.Remove(id, out var doc))
            {
                Logger.LogWarning($"[DocService] Doc_Show: document {id} doesn't exist", LogCat.Ui);
                return;
            }

            Logger.Log($"[DocService] Showing a {(doc.IsMap ? "map" : "document")} with {doc.Pages.Count} pages",
                LogCat.Ui);
            GlobalEventDispatcher.DocShown.Invoke(doc);
        }

        /// <summary>
        /// The map shows the world the hero is in (Doc_SetLevel "WORLD.ZEN", "NewWorld\NewWorld.zen").
        /// </summary>
        public bool IsMapOfCurrentWorld(DocModel doc)
        {
            var world = _saveGameService.CurrentWorldName;
            if (!doc.IsMap || string.IsNullOrEmpty(doc.Level) || string.IsNullOrEmpty(world))
                return false;

            return Path.GetFileName(doc.Level.Replace('\\', '/'))
                .EqualsIgnoreCase(Path.GetFileName(world.Replace('\\', '/')));
        }

        private IEnumerable<DocPage> GetPages(int id, int pageIndex)
        {
            if (!_docs.TryGetValue(id, out var doc))
                yield break;

            for (var i = 0; i < doc.Pages.Count; i++)
            {
                if (pageIndex == -1 || pageIndex == i)
                    yield return doc.Pages[i];
            }
        }

        private static void SetMargins(DocPage page, int[] margins)
        {
            (page.MarginLeft, page.MarginTop, page.MarginRight, page.MarginBottom) =
                (margins[0], margins[1], margins[2], margins[3]);
        }
    }
}
