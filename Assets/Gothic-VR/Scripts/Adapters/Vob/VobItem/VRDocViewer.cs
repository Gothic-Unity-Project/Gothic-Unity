using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Doc;
using Gothic.Core.Services.Caches;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    public class VRDocViewer : MonoBehaviour
    {
        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly Gothic.Core.Services.DocService _docService;

        // The hero's position on a map of the current world (U.TGA arrow, like the engine/OpenGothic).
        private const string _markerTexture = "U.TGA";
        private const float _markerSize = 20f;
        private const string _canvasName = "_DocCanvas";
        // Resources paths (VR context first): world space canvas and one page (background + text).
        private const string _canvasPrefabPath = "Prefabs/UI/DocViewerCanvas";
        private const string _pagePrefabPath = "Prefabs/UI/DocViewerPage";
        private RectTransform _markerPage;
        private RectTransform _marker;
        private float _mapLeft;
        private float _mapTop;
        private float _mapRight;
        private float _mapBottom;

        private void Awake()
        {
            this.Inject();
            GlobalEventDispatcher.DocShow.AddListener(OnDocShow);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.DocShow.RemoveListener(OnDocShow);
        }

        private void OnDocShow(DocModel doc, GameObject itemGo)
        {
            if (itemGo == null || itemGo != gameObject)
                return;

            BuildViewer(doc);
        }

        private void BuildViewer(DocModel doc)
        {
            foreach (Transform child in transform)
            {
                if (child.name == _canvasName)
                    Destroy(child.gameObject);
            }

            if (doc.Pages.Count == 0)
                return;

            var twoPages = doc.Pages.Count >= 2;

            // World space canvas above the item (local position, rotation and scale come with the prefab).
            var canvasGo = _resourceCacheService.TryGetPrefabObject(_canvasPrefabPath, _canvasName, gameObject,
                worldPositionStays: false);
            if (canvasGo == null)
                return;
            var rootRt = (RectTransform)canvasGo.transform;
            rootRt.sizeDelta = twoPages ? new Vector2(1000, 700) : GetSinglePageSize(doc.Pages[0]);

            if (twoPages)
            {
                BuildPage(canvasGo.transform, doc.Pages[0], new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(255, 30), new Vector2(0, -30));
                BuildPage(canvasGo.transform, doc.Pages[1], new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(30, 30), new Vector2(-250, -30));
            }
            else
            {
                BuildPage(canvasGo.transform, doc.Pages[0], Vector2.zero, Vector2.one, new Vector2(50, 30), new Vector2(-50, -30));
            }

            // Gothic textures are uploaded top row first (SetPixelData) - world meshes cancel that out with Gothic's
            // UVs, a UI RawImage showed them upside down (maps looked mirrored).
            foreach (var image in canvasGo.GetComponentsInChildren<RawImage>())
                image.uvRect = new Rect(0f, 1f, 1f, -1f);

            if (!twoPages)
                CreatePlayerMarker(doc, canvasGo.transform.GetChild(0) as RectTransform);

            Logger.Log($"[VRDocViewer] Built viewer: pages={doc.Pages.Count}, twoPages={twoPages}, item={gameObject.name}", LogCat.VR);
        }

        /// <summary>
        /// An arrow where the hero is, turned like him - only on a map of the world
        /// he is in (Doc_SetLevel), placed by the map's edges (Doc_SetLevelCoords, or the world's bounds in G1).
        /// </summary>
        private void CreatePlayerMarker(DocModel doc, RectTransform page)
        {
            _marker = null;
            if (page == null)
                return;
            if (!_docService.IsMapOfCurrentWorld(doc))
            {
                Logger.Log($"[VRDocViewer] Map of '{doc.Level}' isn't the current world - no hero marker", LogCat.VR);
                return;
            }
            if (!_docService.TryGetMapBounds(doc, out _mapLeft, out _mapTop, out _mapRight, out _mapBottom))
                return;

            var markerGo = new GameObject("_PlayerMarker");
            markerGo.transform.SetParent(page, false);
            var image = markerGo.AddComponent<RawImage>();
            var texture = _textureCacheService.TryGetTexture(_markerTexture);
            if (texture != null)
                image.texture = texture;
            else
                image.color = Color.red;

            _marker = image.rectTransform;
            _marker.anchorMin = _marker.anchorMax = Vector2.zero;
            _marker.pivot = new Vector2(0.5f, 0.5f);
            _marker.sizeDelta = Vector2.one * _markerSize;
            _markerPage = page;
            UpdatePlayerMarker();
            // Calibration aid (G1 maps have no coords): where the hero is in Gothic cm when the map opens.
            var heroPosition = GetCamera() != null ? _camera.transform.position * 100f : Vector3.zero;
            Logger.Log($"[VRDocViewer] Map {doc.Level}: hero marker, bounds L{_mapLeft} T{_mapTop} R{_mapRight} " +
                       $"B{_mapBottom}, hero x={heroPosition.x:F0} z={heroPosition.z:F0}", LogCat.VR);
        }

        private Camera _camera;

        private Camera GetCamera()
        {
            if (_camera == null)
                _camera = Camera.main;
            return _camera;
        }

        private void LateUpdate()
        {
            if (_marker != null)
                UpdatePlayerMarker();
        }

        private void UpdatePlayerMarker()
        {
            var head = GetCamera() != null ? _camera.transform : null;
            if (head == null || _markerPage == null)
                return;

            // Gothic cm, top = north (+Z); like OpenGothic's DocumentMenu: (pos - left) / width.
            var position = head.position * 100f;
            var u = (position.x - _mapLeft) / (_mapRight - _mapLeft);
            var v = (position.z - _mapTop) / (_mapBottom - _mapTop);
            var size = _markerPage.rect.size;
            _marker.anchoredPosition = new Vector2(Mathf.Clamp01(u) * size.x, (1f - Mathf.Clamp01(v)) * size.y);

            var forward = head.forward;
            // Map up is +Z in G2 but -Z in G1 (top < bottom): turn the arrow the way the map's axes run.
            var rightSign = _mapRight >= _mapLeft ? 1f : -1f;
            var upSign = _mapTop >= _mapBottom ? 1f : -1f;
            var yaw = Mathf.Atan2(forward.x * rightSign, forward.z * upSign) * Mathf.Rad2Deg;
            _marker.localRotation = Quaternion.Euler(0f, 0f, -yaw);
        }

        private void BuildPage(Transform parent, DocPage page, Vector2 anchorMin, Vector2 anchorMax, Vector2 textOffsetMin, Vector2 textOffsetMax)
        {
            // Page template: background image and auto-sized Gothic font text.
            var pageGo = _resourceCacheService.TryGetPrefabObject(_pagePrefabPath, "_Page", parent.gameObject,
                worldPositionStays: false);
            if (pageGo == null)
                return;
            StretchRect((RectTransform)pageGo.transform, anchorMin, anchorMax);

            var bgImage = pageGo.GetComponentInChildren<RawImage>(true);
            if (string.IsNullOrEmpty(page.Texture))
            {
                Destroy(bgImage.gameObject);
            }
            else
            {
                var tex = _textureCacheService.TryGetTexture(page.Texture);
                if (tex != null)
                    bgImage.texture = tex;
                else
                    bgImage.color = new Color(0.85f, 0.78f, 0.6f);
            }

            var tmp = pageGo.GetComponentInChildren<TMP_Text>(true);
            var textRt = tmp.rectTransform;
            textRt.offsetMin = textOffsetMin;
            textRt.offsetMax = textOffsetMax;
            tmp.text = string.Join("\n", page.Lines);
        }

        private Vector2 GetSinglePageSize(DocPage page)
        {
            if (!string.IsNullOrEmpty(page.Texture))
            {
                var tex = _textureCacheService.TryGetTexture(page.Texture);
                if (tex != null && tex.width > 0 && tex.height > 0)
                {
                    const float maxSize = 700f;
                    var ratio = (float)tex.width / tex.height;
                    return ratio >= 1f
                        ? new Vector2(maxSize, maxSize / ratio)
                        : new Vector2(maxSize * ratio, maxSize);
                }
            }
            return new Vector2(500, 700);
        }

        private static void StretchRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax)
        {
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
