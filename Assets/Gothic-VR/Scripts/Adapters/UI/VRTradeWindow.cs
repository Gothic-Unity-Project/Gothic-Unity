using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.UI;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Player;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// Gothic's trade screen in front of the player (TradeService): the trader's goods on the left, the hero's on the
    /// right, each with its price, the hero's money below. Clicking a row with the UI laser buys or sells one piece at
    /// once. Arrows page through longer lists, X ends trading and the dialog goes on. Items the hero holds in his hands
    /// aren't offered for sale.
    /// </summary>
    public class VRTradeWindow : MonoBehaviour
    {
        private const int _rowsPerPage = 8;
        private const float _distance = 1.0f;
        private const float _heightBelowEyes = 0.3f;
        // One canvas unit is one millimetre.
        private const float _canvasScale = 0.001f;
        private const float _width = 1000f;
        private const float _height = 720f;
        private const float _columnWidth = 470f;
        private const float _rowHeight = 58f;
        private const float _fontSize = 30f;
        private const float _disabledAlpha = 0.4f;
        private static readonly Color _backgroundColor = new(0f, 0f, 0f, 0.75f);
        private static readonly Color _rowColor = new(1f, 1f, 1f, 0.06f);

        private static VRTradeWindow _current;

        private TradeService _tradeService;
        private VmCacheService _vmCacheService;
        private VRPlayerService _vrPlayerService;
        private NpcContainer _trader;
        private NpcContainer _hero;
        private Canvas _canvas;
        private TMP_Text _currencyText;
        private readonly List<Row> _traderRows = new();
        private readonly List<Row> _heroRows = new();
        private int _traderPage;
        private int _heroPage;

        private class Row
        {
            public Button Button;
            public TMP_Text Name;
            public TMP_Text Price;
            public string ItemName;
        }

        public static void Show(NpcContainer trader, NpcContainer hero, TradeService tradeService,
            VmCacheService vmCacheService, VRPlayerService vrPlayerService)
        {
            Hide();

            var go = new GameObject("VR Trade Window", typeof(RectTransform))
            {
                layer = LayerMask.NameToLayer("UI")
            };
            _current = go.AddComponent<VRTradeWindow>();
            _current.Init(trader, hero, tradeService, vmCacheService, vrPlayerService);
        }

        public static void Hide()
        {
            if (_current == null)
                return;

            ReflexProjectInstaller.DIContainer.Resolve<IVrUiPointers>().UnregisterCanvas(_current._canvas);
            Destroy(_current.gameObject);
            _current = null;
        }

        private void Init(NpcContainer trader, NpcContainer hero, TradeService tradeService,
            VmCacheService vmCacheService, VRPlayerService vrPlayerService)
        {
            _trader = trader;
            _hero = hero;
            _tradeService = tradeService;
            _vmCacheService = vmCacheService;
            _vrPlayerService = vrPlayerService;

            BuildCanvas();
            PlaceInFrontOfCamera();
            Refresh();

            // A canvas created at runtime: the UI laser only hits registered canvases.
            ReflexProjectInstaller.DIContainer.Resolve<IVrUiPointers>().RegisterCanvas(_canvas);
            Logger.Log("[VRTradeWindow] Shown", LogCat.Ui);
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

        private void BuildCanvas()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(_width, _height);
            rect.localScale = Vector3.one * _canvasScale;

            var background = gameObject.AddComponent<Image>();
            background.color = _backgroundColor;

            var top = _height / 2f - _rowHeight;
            var left = -_width / 4f;
            var right = _width / 4f;
            CreateText(_trader.Instance.GetName(NpcNameSlot.Slot0), new Vector2(left, top), _columnWidth);
            CreateText(_hero.Instance.GetName(NpcNameSlot.Slot0), new Vector2(right, top), _columnWidth);

            for (var i = 0; i < _rowsPerPage; i++)
            {
                var y = top - (i + 1.2f) * _rowHeight;
                _traderRows.Add(CreateRow(new Vector2(left, y), OnTraderRowClicked));
                _heroRows.Add(CreateRow(new Vector2(right, y), OnHeroRowClicked));
            }

            var bottom = -_height / 2f + _rowHeight;
            CreateButton("<", new Vector2(left - 120f, bottom), 90f, () => TurnPage(ref _traderPage, -1));
            CreateButton(">", new Vector2(left + 120f, bottom), 90f, () => TurnPage(ref _traderPage, 1));
            CreateButton("<", new Vector2(right - 120f, bottom), 90f, () => TurnPage(ref _heroPage, -1));
            CreateButton(">", new Vector2(right + 120f, bottom), 90f, () => TurnPage(ref _heroPage, 1));
            CreateButton("X", new Vector2(_width / 2f - 50f, _height / 2f - 50f), 70f, () => _tradeService.End());
            _currencyText = CreateText(string.Empty, new Vector2(0f, bottom), 300f);
            _currencyText.horizontalAlignment = HorizontalAlignmentOptions.Center;
        }

        private Row CreateRow(Vector2 position, System.Action<Row> onClick)
        {
            var row = new Row();
            var button = CreateButton(string.Empty, position, _columnWidth, null);
            button.GetComponent<Image>().color = _rowColor;
            row.Button = button;
            row.Name = button.GetComponentInChildren<TMP_Text>();
            row.Name.horizontalAlignment = HorizontalAlignmentOptions.Left;

            row.Price = CreateText(string.Empty, Vector2.zero, _columnWidth - 20f, button.transform);
            row.Price.horizontalAlignment = HorizontalAlignmentOptions.Right;
            button.onClick.AddListener(() => onClick(row));
            return row;
        }

        private Button CreateButton(string label, Vector2 position, float width, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button))
            {
                layer = gameObject.layer
            };
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, _rowHeight * 0.9f);
            go.GetComponent<Image>().color = _rowColor;

            CreateText(label, Vector2.zero, width - 20f, rect).horizontalAlignment =
                HorizontalAlignmentOptions.Center;
            var button = go.GetComponent<Button>();
            if (onClick != null)
                button.onClick.AddListener(onClick);
            return button;
        }

        private TMP_Text CreateText(string content, Vector2 position, float width, Transform parent = null)
        {
            var go = new GameObject("Text", typeof(RectTransform))
            {
                layer = gameObject.layer
            };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent != null ? parent : transform, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, _rowHeight);

            var text = go.AddComponent<TextMeshProUGUI>();
            GothicText.Apply(text);
            text.fontSize = _fontSize;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.text = content;
            return text;
        }

        private void TurnPage(ref int page, int direction)
        {
            page = Mathf.Max(0, page + direction);
            Refresh();
        }

        private void OnTraderRowClicked(Row row)
        {
            if (row.ItemName != null && _tradeService.Buy(row.ItemName))
                Refresh();
        }

        private void OnHeroRowClicked(Row row)
        {
            if (row.ItemName != null && _tradeService.Sell(row.ItemName))
                Refresh();
        }

        private void Refresh()
        {
            var traderGoods = _tradeService.GetGoods(_trader);
            var heroGoods = GetHeroGoods();
            _traderPage = ClampPage(_traderPage, traderGoods.Count);
            _heroPage = ClampPage(_heroPage, heroGoods.Count);

            FillRows(_traderRows, traderGoods, _traderPage, true);
            FillRows(_heroRows, heroGoods, _heroPage, false);

            var currency = _vmCacheService.TryGetItemData(_tradeService.CurrencyName);
            _currencyText.text = $"{currency?.Name}: {_tradeService.GetCurrencyAmount(_hero)}";
        }

        private static int ClampPage(int page, int count)
        {
            var lastPage = Mathf.Max(0, (count - 1) / _rowsPerPage);
            return Mathf.Min(page, lastPage);
        }

        private void FillRows(List<Row> rows, List<ContentItem> goods, int page, bool isBuying)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var index = page * _rowsPerPage + i;
                if (index >= goods.Count)
                {
                    row.ItemName = null;
                    row.Button.gameObject.SetActive(false);
                    continue;
                }

                var good = goods[index];
                var itemData = _vmCacheService.TryGetItemData(good.Name);
                var price = isBuying ? _tradeService.GetBuyPrice(good.Name) : _tradeService.GetSellPrice(good.Name);
                var isPossible = !isBuying || _tradeService.CanBuy(good.Name);

                row.ItemName = good.Name;
                row.Button.gameObject.SetActive(true);
                row.Button.interactable = isPossible;
                row.Name.text = good.Amount > 1 ? $"{itemData?.Name} x{good.Amount}" : itemData?.Name;
                row.Price.text = price.ToString();
                var alpha = isPossible ? 1f : _disabledAlpha;
                row.Name.alpha = alpha;
                row.Price.alpha = alpha;
            }
        }

        /// <summary>
        /// The hero's goods without what he holds in his hands: those are in his inventory too, but selling one would
        /// leave it in the hand.
        /// </summary>
        private List<ContentItem> GetHeroGoods()
        {
            var goods = _tradeService.GetGoods(_hero);
            foreach (var held in new[] { _vrPlayerService.GrabbedItemLeft, _vrPlayerService.GrabbedItemRight }.Distinct())
            {
                var item = held != null ? held.GetComponentInParent<VobLoader>()?.Container?.VobAs<IItem>() : null;
                if (item == null)
                    continue;

                var name = !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
                var good = goods.FirstOrDefault(g => g.Name.EqualsIgnoreCase(name));
                if (good == null)
                    continue;

                good.Amount -= item.Amount;
                if (good.Amount <= 0)
                    goods.Remove(good);
            }

            return goods;
        }

        private void OnDestroy()
        {
            if (_current == this)
                _current = null;
        }
    }
}
