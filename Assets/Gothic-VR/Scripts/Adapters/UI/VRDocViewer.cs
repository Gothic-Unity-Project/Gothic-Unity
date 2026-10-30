using Gothic.Core.Adapters.UI;
using Gothic.Core.Logging;
using Gothic.Core.Models.Doc;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// A book, letter or map in front of the player (DocModel from Doc_Show): its pages side by side, each with its
    /// background texture and its lines in the page's Gothic font inside the page's margins. A map of the world the
    /// hero is in marks where he is, if the map knows its world coordinates (G2's Doc_SetLevelCoords).
    /// </summary>
    public class VRDocViewer : MonoBehaviour
    {
        private const float _distance = 0.6f;
        private const float _heightBelowEyes = 0.15f;
        private const float _pageHeight = 0.45f;
        private const float _mapHeight = 0.6f;
        // Font size range in page pixels: as large as the page's margins allow.
        private const float _maxFontSize = 22f;
        private const float _minFontSize = 6f;
        private const float _markerSize = 24f;
        private const float _fallbackPagePixels = 512f;

        private static VRDocViewer _current;

        public static bool IsShown => _current != null;

        public static void Show(DocModel doc, DocService docService, TextureCacheService textureCacheService,
            FontService fontService, Transform hero)
        {
            Hide();

            var go = new GameObject("VR Doc Viewer", typeof(RectTransform))
            {
                layer = LayerMask.NameToLayer("UI")
            };
            _current = go.AddComponent<VRDocViewer>();
            _current.Build(doc, docService, textureCacheService, fontService, hero);
        }

        public static void Hide()
        {
            if (_current == null)
                return;

            Destroy(_current.gameObject);
            _current = null;
        }

        private void Build(DocModel doc, DocService docService, TextureCacheService textureCacheService,
            FontService fontService, Transform hero)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // One canvas unit is one pixel of the first page's texture.
            var firstTexture = doc.Pages.Count > 0 ? textureCacheService.TryGetTexture(doc.Pages[0].Texture) : null;
            var pagePixels = firstTexture != null
                ? new Vector2(firstTexture.width, firstTexture.height)
                : new Vector2(_fallbackPagePixels, _fallbackPagePixels);
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(pagePixels.x * Mathf.Max(1, doc.Pages.Count), pagePixels.y);
            rect.localScale = Vector3.one * ((doc.IsMap ? _mapHeight : _pageHeight) / pagePixels.y);

            for (var i = 0; i < doc.Pages.Count; i++)
            {
                var x = (i - (doc.Pages.Count - 1) / 2f) * pagePixels.x;
                var page = CreatePage(doc.Pages[i], new Vector2(x, 0f), pagePixels, textureCacheService, fontService);
                if (i == 0 && doc.IsMap && docService.IsMapOfCurrentWorld(doc) && doc.HasLevelCoords)
                    AddHeroMarker(page, doc, pagePixels, hero);
            }

            PlaceInFrontOfCamera();
            Logger.Log($"[VRDocViewer] Shown with {doc.Pages.Count} pages", LogCat.Ui);
        }

        private RectTransform CreatePage(DocPage page, Vector2 position, Vector2 size,
            TextureCacheService textureCacheService, FontService fontService)
        {
            var pageGo = new GameObject("Page", typeof(RectTransform), typeof(RawImage))
            {
                layer = gameObject.layer
            };
            var pageRect = (RectTransform)pageGo.transform;
            pageRect.SetParent(transform, false);
            pageRect.anchoredPosition = position;
            pageRect.sizeDelta = size;
            pageGo.GetComponent<RawImage>().texture = textureCacheService.TryGetTexture(page.Texture);

            if (page.Lines.Count == 0)
                return pageRect;

            var textGo = new GameObject("Text", typeof(RectTransform))
            {
                layer = gameObject.layer
            };
            var textRect = (RectTransform)textGo.transform;
            textRect.SetParent(pageRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(page.MarginLeft, page.MarginBottom);
            textRect.offsetMax = new Vector2(-page.MarginRight, -page.MarginTop);

            var text = textGo.AddComponent<TextMeshProUGUI>();
            GothicText.Apply(text);
            if (!string.IsNullOrEmpty(page.Font))
                text.spriteAsset = fontService.TryGetFont(page.Font) ?? fontService.DefaultSpriteAsset;
            text.enableAutoSizing = true;
            text.fontSizeMax = _maxFontSize;
            text.fontSizeMin = _minFontSize;
            text.horizontalAlignment = HorizontalAlignmentOptions.Left;
            text.verticalAlignment = VerticalAlignmentOptions.Top;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            text.text = string.Join("\n", page.Lines);
            return pageRect;
        }

        /// <summary>
        /// The hero's place on the map, a red diamond. The map's edges are world coordinates in cm, north (top) is +Z.
        /// </summary>
        private void AddHeroMarker(RectTransform page, DocModel doc, Vector2 pagePixels, Transform hero)
        {
            if (hero == null || doc.Right == doc.Left || doc.Bottom == doc.Top)
                return;

            var x = (hero.position.x * 100f - doc.Left) / (doc.Right - doc.Left);
            var y = (hero.position.z * 100f - doc.Bottom) / (doc.Top - doc.Bottom);
            if (x < 0f || x > 1f || y < 0f || y > 1f)
                return;

            var markerGo = new GameObject("Hero", typeof(RectTransform), typeof(Image))
            {
                layer = gameObject.layer
            };
            var markerRect = (RectTransform)markerGo.transform;
            markerRect.SetParent(page, false);
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(x, y);
            markerRect.anchoredPosition = Vector2.zero;
            markerRect.sizeDelta = new Vector2(_markerSize, _markerSize);
            markerRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            markerGo.GetComponent<Image>().color = Color.red;
        }

        private void PlaceInFrontOfCamera()
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            var forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            transform.position = cam.transform.position + forward * _distance + Vector3.down * _heightBelowEyes;
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }

        private void OnDestroy()
        {
            if (_current == this)
                _current = null;
        }
    }
}
