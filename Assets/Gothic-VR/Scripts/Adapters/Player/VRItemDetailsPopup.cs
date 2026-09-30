#if GOTHIC_HVR_INSTALLED
using System.Text;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Config;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZenKit.Daedalus;
using ZenKit.Vobs;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableItemDetailsPopup): "backpack mode" - while the backpack is held in one hand and an item
    /// in the other, a popup above the item shows what Gothic's inventory shows: description, amount and the
    /// C_Item text[]/count[] rows (damage, protection, required attributes, value, ...).
    /// Added at runtime by VRBackpack.
    /// </summary>
    public class VRItemDetailsPopup : MonoBehaviour
    {
        // C_Item has 6 text[]/count[] rows.
        private const int _textRowCount = 6;
        private const float _canvasScale = 0.001f;
        private const float _heightAboveItem = 0.18f;
        private const float _updateInterval = 0.1f;

        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        private HVRGrabbable _backpackGrabbable;
        private GameObject _canvasGo;
        private TMP_Text _text;
        private GameObject _shownItem;
        private float _updateTimer;


        private void Awake()
        {
            this.Inject();
        }

        private void Start()
        {
            _backpackGrabbable = GetComponent<HVRGrabbable>();
            BuildCanvas();
            _canvasGo.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_canvasGo != null)
                Destroy(_canvasGo);
        }

        private void LateUpdate()
        {
            _updateTimer += Time.deltaTime;
            if (_updateTimer >= _updateInterval)
            {
                _updateTimer = 0f;
                UpdateShownItem();
            }

            if (_shownItem != null)
                FollowItem();
        }

        private void UpdateShownItem()
        {
            GameObject item = null;
            if (_configService.Dev.EnableItemDetailsPopup && _backpackGrabbable != null && _backpackGrabbable.IsHandGrabbed)
                item = _vrPlayerService.GrabbedItemLeft != null ? _vrPlayerService.GrabbedItemLeft : _vrPlayerService.GrabbedItemRight;

            if (item == _shownItem)
                return;

            _shownItem = item;
            if (item == null || !TrySetText(item))
            {
                _shownItem = null;
                _canvasGo.SetActive(false);
                return;
            }

            _canvasGo.SetActive(true);
        }

        private bool TrySetText(GameObject itemGo)
        {
            var container = itemGo.GetComponentInParent<VobLoader>()?.Container;
            if (container == null || container.Vob.Type != VirtualObjectType.oCItem)
                return false;

            var item = container.PropsAs<VobItemProperties2>()?.Instance;
            if (item == null)
                return false;

            var title = string.IsNullOrEmpty(item.Description) ? item.Name : item.Description;
            var amount = container.Vob is IItem vobItem ? vobItem.Amount : 1;

            var sb = new StringBuilder();
            sb.Append("<b>").Append(title).Append("</b>");
            if (amount > 1)
                sb.Append("  x").Append(amount);
            sb.Append('\n');

            AppendTextRows(sb, item);

            _text.text = sb.ToString();
            return true;
        }

        /// <summary>
        /// Like Gothic's inventory: a row is shown when its text is set, the count only when it's not 0.
        /// </summary>
        private static void AppendTextRows(StringBuilder sb, ItemInstance item)
        {
            for (var i = 0; i < _textRowCount; i++)
            {
                var slot = (ItemTextSlot)i;
                var text = item.GetText(slot);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var count = item.GetCount(slot);
                sb.Append('\n').Append(text.Trim());
                if (count != 0)
                    sb.Append("<pos=75%>").Append(count);
            }
        }

        private void FollowItem()
        {
            var cam = Camera.main;
            _canvasGo.transform.position = _shownItem.transform.position + Vector3.up * _heightAboveItem;
            if (cam != null)
                _canvasGo.transform.rotation = Quaternion.LookRotation(_canvasGo.transform.position - cam.transform.position);
        }

        private void BuildCanvas()
        {
            // Not parented to the backpack: it follows the item in world space and must not inherit the backpack's scale.
            _canvasGo = new GameObject("_ItemDetailsPopup");
            _canvasGo.transform.localScale = Vector3.one * _canvasScale;

            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rootRt = canvas.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(360, 200);
            rootRt.pivot = new Vector2(0.5f, 0f);

            var bgGo = new GameObject("_BG");
            bgGo.transform.SetParent(_canvasGo.transform, false);
            var bg = bgGo.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.75f);
            Stretch(bg.rectTransform, Vector2.zero, Vector2.zero);

            var textGo = new GameObject("_Text");
            textGo.transform.SetParent(_canvasGo.transform, false);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();

            var font = Resources.Load<TMP_FontAsset>("FontAsset/LiberationSans SDF empty");
            if (font != null)
                tmp.font = font;

            Stretch(tmp.rectTransform, new Vector2(15, 10), new Vector2(-15, -10));
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 10;
            tmp.fontSizeMax = 22;
            tmp.color = Color.white;
            tmp.richText = true;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            _text = tmp;
        }

        private static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
    }
}
#endif
