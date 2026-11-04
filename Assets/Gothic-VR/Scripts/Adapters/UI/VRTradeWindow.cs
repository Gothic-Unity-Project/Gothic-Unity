using System.Collections.Generic;
using System.Linq;
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
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// Gothic's trade screen in front of the player (TradeService), prefab VR/Prefabs/UI/TradeWindow: the trader's goods
    /// on the left, the hero's on the right, each with its price, the hero's money below. Clicking a row with the UI
    /// laser buys or sells one piece at once. Arrows page through longer lists, X ends trading and the dialog goes on.
    /// Items the hero holds in his hands aren't offered for sale. Shown and destroyed by VRContextTradeService.
    /// </summary>
    public class VRTradeWindow : MonoBehaviour
    {
        [SerializeField] private Canvas _canvas;
        [SerializeField] private TMP_Text _traderName;
        [SerializeField] private TMP_Text _heroName;
        [SerializeField] private TMP_Text _currencyText;

        [Tooltip("Inactive row, cloned RowsPerPage times into each column.")]
        [SerializeField] private VRTradeRow _rowTemplate;
        [SerializeField] private RectTransform _traderRowsRoot;
        [SerializeField] private RectTransform _heroRowsRoot;

        [SerializeField] private Button _traderPreviousButton;
        [SerializeField] private Button _traderNextButton;
        [SerializeField] private Button _heroPreviousButton;
        [SerializeField] private Button _heroNextButton;
        [SerializeField] private Button _closeButton;

        [Tooltip("Rows per column and page.")]
        [SerializeField] private int _rowsPerPage = 8;

        [Inject] private readonly TradeService _tradeService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly IVrUiPointers _vrUiPointers;

        private NpcContainer _trader;
        private NpcContainer _hero;
        private readonly List<VRTradeRow> _traderRows = new();
        private readonly List<VRTradeRow> _heroRows = new();
        private int _traderPage;
        private int _heroPage;

        public void Show(NpcContainer trader, NpcContainer hero)
        {
            _trader = trader;
            _hero = hero;

            _rowTemplate.gameObject.SetActive(false);
            CreateRows(_traderRowsRoot, _traderRows, OnTraderRowClicked);
            CreateRows(_heroRowsRoot, _heroRows, OnHeroRowClicked);

            _traderPreviousButton.onClick.AddListener(() => TurnPage(ref _traderPage, -1));
            _traderNextButton.onClick.AddListener(() => TurnPage(ref _traderPage, 1));
            _heroPreviousButton.onClick.AddListener(() => TurnPage(ref _heroPage, -1));
            _heroNextButton.onClick.AddListener(() => TurnPage(ref _heroPage, 1));
            _closeButton.onClick.AddListener(() => _tradeService.End());

            // The game's font exists only at runtime (FontService).
            foreach (var text in GetComponentsInChildren<TMP_Text>(true))
                GothicText.Apply(text);

            _traderName.text = trader.Instance.GetName(NpcNameSlot.Slot0);
            _heroName.text = hero.Instance.GetName(NpcNameSlot.Slot0);
            Refresh();

            // Instantiated at runtime: the UI laser only hits registered canvases.
            _vrUiPointers.RegisterCanvas(_canvas);
            Logger.Log("[VRTradeWindow] Shown", LogCat.Ui);
        }

        private void CreateRows(RectTransform root, List<VRTradeRow> rows, System.Action<VRTradeRow> onClick)
        {
            for (var i = 0; i < _rowsPerPage; i++)
            {
                var row = Instantiate(_rowTemplate, root, false);
                row.name = $"Row {i}";
                row.Button.onClick.AddListener(() => onClick(row));
                rows.Add(row);
            }
        }

        private void TurnPage(ref int page, int direction)
        {
            page = Mathf.Max(0, page + direction);
            Refresh();
        }

        private void OnTraderRowClicked(VRTradeRow row)
        {
            if (row.ItemName != null && _tradeService.Buy(row.ItemName))
                Refresh();
        }

        private void OnHeroRowClicked(VRTradeRow row)
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

        private int ClampPage(int page, int count)
        {
            var lastPage = Mathf.Max(0, (count - 1) / _rowsPerPage);
            return Mathf.Min(page, lastPage);
        }

        private void FillRows(List<VRTradeRow> rows, List<ContentItem> goods, int page, bool isBuying)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var index = page * _rowsPerPage + i;
                if (index >= goods.Count)
                {
                    rows[i].Clear();
                    continue;
                }

                var good = goods[index];
                var itemName = _vmCacheService.TryGetItemData(good.Name)?.Name;
                var price = isBuying ? _tradeService.GetBuyPrice(good.Name) : _tradeService.GetSellPrice(good.Name);
                var isPossible = !isBuying || _tradeService.CanBuy(good.Name);
                var label = good.Amount > 1 ? $"{itemName} x{good.Amount}" : itemName;
                rows[i].Show(good.Name, label, price, isPossible);
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
            _vrUiPointers?.UnregisterCanvas(_canvas);
        }
    }
}
