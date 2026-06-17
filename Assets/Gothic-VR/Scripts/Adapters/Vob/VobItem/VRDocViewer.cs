#if GOTHIC_HVR_INSTALLED
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
                if (child.name == "_DocCanvas")
                    Destroy(child.gameObject);
            }

            if (doc.Pages.Count == 0)
                return;

            var twoPages = doc.Pages.Count >= 2;

            var canvasGo = new GameObject("_DocCanvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);
            canvasGo.transform.localPosition = new Vector3(0, 0.1f, 0);
            canvasGo.transform.localRotation = Quaternion.Euler(90, 0, 0);
            canvasGo.transform.localScale = Vector3.one * 0.0012f;

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rootRt = canvas.GetComponent<RectTransform>();
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = twoPages ? new Vector2(1000, 700) : GetSinglePageSize(doc.Pages[0]);

            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            if (twoPages)
            {
                BuildPage(canvasGo.transform, doc.Pages[0], new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(255, 30), new Vector2(0, -30));
                BuildPage(canvasGo.transform, doc.Pages[1], new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(30, 30), new Vector2(-250, -30));
            }
            else
            {
                BuildPage(canvasGo.transform, doc.Pages[0], Vector2.zero, Vector2.one, new Vector2(50, 30), new Vector2(-50, -30));
            }

            // FIXME: Maps need to track player position in world and track it on map
            Logger.Log($"[VRDocViewer] Built viewer: pages={doc.Pages.Count}, twoPages={twoPages}, item={gameObject.name}", LogCat.VR);
        }

        private void BuildPage(Transform parent, DocPage page, Vector2 anchorMin, Vector2 anchorMax, Vector2 textOffsetMin, Vector2 textOffsetMax)
        {
            var pageGo = new GameObject("_Page");
            pageGo.transform.SetParent(parent, false);
            var pageRt = pageGo.AddComponent<RectTransform>();
            StretchRect(pageRt, anchorMin, anchorMax);

            if (!string.IsNullOrEmpty(page.Texture))
            {
                var bgGo = new GameObject("_BG");
                bgGo.transform.SetParent(pageGo.transform, false);
                var bgImage = bgGo.AddComponent<RawImage>();
                StretchRect(bgImage.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);

                var tex = _textureCacheService.TryGetTexture(page.Texture);
                if (tex != null)
                    bgImage.texture = tex;
                else
                    bgImage.color = new Color(0.85f, 0.78f, 0.6f);
            }

            var textGo = new GameObject("_Text");
            textGo.transform.SetParent(pageGo.transform, false);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();

            var font = Resources.Load<TMP_FontAsset>("FontAsset/LiberationSans SDF empty");
            if (font != null)
                tmp.font = font;

            var textRt = tmp.GetComponent<RectTransform>();
            textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;
            textRt.offsetMin = textOffsetMin;
            textRt.offsetMax = textOffsetMax;

            tmp.text = string.Join("\n", page.Lines);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 14;
            tmp.fontSizeMax = 30;
            tmp.color = Color.black;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.alignment = TextAlignmentOptions.TopLeft;
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
#endif
