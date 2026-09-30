#if GOTHIC_HVR_INSTALLED
using System.Text;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Config;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZenKit.Daedalus;
using ZenKit.Vobs;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// V2 (DeveloperConfig.EnableItemDetailsPopup): "backpack mode" - while the backpack is held in one hand and the other
    /// hand holds an item (or hovers one inside the backpack), a popup above the item shows what Gothic's inventory
    /// shows: description, amount and the C_Item text[]/count[] rows (damage, protection, required attributes, value, ...).
    /// Added at runtime by VRBackpack.
    /// </summary>
    public class VRItemDetailsPopup : MonoBehaviour
    {
        // C_Item has 6 text[]/count[] rows.
        private const int _textRowCount = 6;
        private const float _canvasScale = 0.001f;
        private const float _heightAboveItem = 0.05f;
        private const float _pullTowardsCamera = 0.15f;
        // Max distance between the free hand's palm and a backpack slot item to show its popup.
        private const float _hoverDistance = 0.12f;
        private const float _updateInterval = 0.1f;

        [Inject] private readonly ConfigService _configService;

        private HVRGrabbable _backpackGrabbable;
        private HVRHandGrabber[] _hands;
        private GameObject _canvasGo;
        private TMP_Text _text;
        private GameObject _shownItem;
        private Renderer[] _shownItemRenderers;
        private float _updateTimer;


        private void Awake()
        {
            this.Inject();
        }

        private void Start()
        {
            _backpackGrabbable = GetComponent<HVRGrabbable>();
            _hands = FindObjectsByType<HVRHandGrabber>(FindObjectsSortMode.None);
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

        /// <summary>
        /// Re-evaluated every tick (not only on change): the text refreshes e.g. the amount after eating from a stack,
        /// and the popup hides as soon as HVR says the item isn't held/hovered anymore (e.g. stored in the backpack).
        /// </summary>
        private void UpdateShownItem()
        {
            var item = FindItemToShow();
            if (item == null || !TrySetText(item))
            {
                _shownItem = null;
                _canvasGo.SetActive(false);
                return;
            }

            if (item != _shownItem)
            {
                _shownItem = item;
                var root = item.GetComponentInParent<VobLoader>();
                _shownItemRenderers = (root != null ? root.gameObject : item).GetComponentsInChildren<Renderer>();
            }

            _canvasGo.SetActive(true);
        }

        /// <summary>
        /// Backpack in one hand. The other hand either holds an item or hovers one inside the backpack's slots.
        /// </summary>
        private GameObject FindItemToShow()
        {
            if (!_configService.Dev.EnableItemDetailsPopup || _backpackGrabbable == null || !_backpackGrabbable.IsHandGrabbed)
                return null;

            foreach (var hand in _hands)
            {
                if (hand == null || hand.GrabbedTarget == _backpackGrabbable)
                    continue;

                var held = hand.GrabbedTarget;
                if (held != null)
                    return IsItem(held) ? held.gameObject : null;

                var hovered = hand.HoverTarget;
                if (hovered != null && hovered.transform.IsChildOf(transform) && IsItem(hovered))
                    return hovered.gameObject;

                // HVR doesn't report socketed backpack items as HoverTarget - fall back to the closest one near the palm.
                var palm = hand.PhysicsPoser != null ? hand.Palm : null;
                var near = FindBackpackItemNear(palm != null ? palm.position : hand.transform.position);
                if (near != null)
                    return near.gameObject;
            }

            return null;
        }

        private HVRGrabbable FindBackpackItemNear(Vector3 position)
        {
            HVRGrabbable closest = null;
            var closestDistance = _hoverDistance;

            foreach (var socket in GetComponentsInChildren<HVRSocket>())
            {
                var item = socket.GrabbedTarget;
                if (item == null || !IsItem(item))
                    continue;

                var distance = Vector3.Distance(item.transform.position, position);
                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                closest = item;
            }

            return closest;
        }

        private static bool IsItem(HVRGrabbable grabbable)
        {
            var container = grabbable.GetComponentInParent<VobLoader>()?.Container;
            return container != null && container.Vob.Type == VirtualObjectType.oCItem;
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

        /// <summary>
        /// Placed above the item's rendered bounds (not its pivot) - big items like armor would cover it otherwise -
        /// and pulled a bit towards the camera so it doesn't end up inside the item.
        /// </summary>
        private void FollowItem()
        {
            var cam = Camera.main;
            var anchor = _shownItem.transform.position;

            if (_shownItemRenderers is { Length: > 0 })
            {
                var bounds = new Bounds();
                var hasBounds = false;
                foreach (var r in _shownItemRenderers)
                {
                    if (r == null || !r.enabled)
                        continue;
                    if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                    else bounds.Encapsulate(r.bounds);
                }

                if (hasBounds)
                    anchor = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            }

            var position = anchor + Vector3.up * _heightAboveItem;
            if (cam != null)
                position += (cam.transform.position - position).normalized * _pullTowardsCamera;

            _canvasGo.transform.position = position;
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
