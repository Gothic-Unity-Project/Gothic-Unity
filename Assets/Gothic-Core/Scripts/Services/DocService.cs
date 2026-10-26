using System.Collections.Generic;
using System.IO;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Doc;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services
{
    public class DocService
    {
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly Gothic.Core.Services.Config.ConfigService _configService;

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

        public void SetLevel(int id, string level)
        {
            var doc = Get(id);
            if (doc != null)
                doc.Level = level;
            Logger.Log($"[DocService] Doc_SetLevel id={id} level={level}", LogCat.Npc);
        }

        public void SetLevelCoords(int id, int left, int top, int right, int bottom)
        {
            var doc = Get(id);
            if (doc == null)
                return;
            doc.HasLevelCoords = true;
            doc.Left = left;
            doc.Top = top;
            doc.Right = right;
            doc.Bottom = bottom;
        }

        /// <summary>
        /// The map belongs to the world the hero is in (Doc_SetLevel "WORLD.ZEN", "Addon\AddonWorld.zen").
        /// </summary>
        public bool IsMapOfCurrentWorld(DocModel doc)
        {
            if (doc?.IsMap != true || string.IsNullOrEmpty(doc.Level) || string.IsNullOrEmpty(_saveGameService.CurrentWorldName))
                return false;
            return Path.GetFileName(doc.Level.Replace('\\', '/')).EqualsIgnoreCase(
                Path.GetFileName(_saveGameService.CurrentWorldName.Replace('\\', '/')));
        }

        /// <summary>
        /// World coordinates (cm) of the map edges: left, top, right, bottom like Doc_SetLevelCoords (top = north = +Z).
        /// G1 maps set none - the engine uses the world mesh's bounds then.
        /// </summary>
        public bool TryGetMapBounds(DocModel doc, out float left, out float top, out float right, out float bottom)
        {
            left = top = right = bottom = 0f;
            if (doc.HasLevelCoords)
            {
                (left, top, right, bottom) = (doc.Left, doc.Top, doc.Right, doc.Bottom);
                return right != left && bottom != top;
            }

            // G1 maps set no coords and the world mesh's bounds don't match the drawing (the hero was ~12% off) -
            // calibrated edges per world from the config (left, top, right, bottom in cm).
            var calibrated = _configService.Dev.G1WorldMapBounds;
            if (doc.Level != null && Path.GetFileName(doc.Level.Replace('\\', '/')).EqualsIgnoreCase("WORLD.ZEN") &&
                calibrated.x != calibrated.z && calibrated.y != calibrated.w)
            {
                (left, top, right, bottom) = (calibrated.x, calibrated.y, calibrated.z, calibrated.w);
                return true;
            }

            try
            {
                var box = _saveGameService.CurrentWorldData.Mesh.BoundingBox;
                (left, top, right, bottom) = (box.Min.X, box.Max.Z, box.Max.X, box.Min.Z);
                if (right != left && bottom != top)
                    return true;
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"[DocService] World mesh bounds unavailable: {e.Message}", LogCat.Npc);
            }

            // The built world mesh in the scene (meters -> cm).
            var worldRoot = GameObject.Find("World");
            var renderers = worldRoot != null ? worldRoot.GetComponentsInChildren<Renderer>(true) : null;
            if (renderers == null || renderers.Length == 0)
            {
                Logger.LogWarning("[DocService] No world bounds for the map", LogCat.Npc);
                return false;
            }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            (left, top, right, bottom) = (bounds.min.x * 100f, bounds.max.z * 100f, bounds.max.x * 100f,
                bounds.min.z * 100f);
            return right != left && bottom != top;
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
