using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Player
{
    /// <summary>
    /// Trading with an NPC, like the engine (OpenGothic Npc::buyItem/sellItem): a dialog entry with C_Info.trade opens
    /// it after the NPC's lines, and the dialog's choices come back once it ends. Each item bought or sold changes hands
    /// at once: the hero pays the item's value, and gets the value times TRADE_VALUE_MULTIPLIER (G2) for what he sells.
    /// The trader's own money doesn't change, as in the engine. Neither side trades equipped items or the currency.
    /// </summary>
    public class TradeService
    {
        // G1 trades with ore. G2 names its currency in TRADE_CURRENCY_INSTANCE.
        private const string _g1Currency = "ITMINUGGET";

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        private int _returnInformation;
        private DaedalusVm _currencyVm;
        private string _currencyName;
        private float _sellMultiplier = 1f;

        public NpcContainer Trader { get; private set; }

        public bool IsTrading => Trader != null;

        /// <summary>
        /// Instance name of the money item (G1 ore, G2 gold).
        /// </summary>
        public string CurrencyName
        {
            get
            {
                ReadScriptValues();
                return _currencyName;
            }
        }

        /// <param name="returnInformation">The dialog entry that opened it - its choices come back afterwards.</param>
        public void Start(NpcContainer trader, int returnInformation)
        {
            if (IsTrading)
                End(false);

            Trader = trader;
            _returnInformation = returnInformation;
            Logger.Log($"[Trade] Trading with {trader.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Dialog);
            GlobalEventDispatcher.TradeStarted.Invoke(trader);
        }

        /// <param name="isDialogResumed">The dialog goes on with its choices (the player closed the trade), not when
        /// the dialog itself ended.</param>
        public void End(bool isDialogResumed = true)
        {
            if (!IsTrading)
                return;

            var trader = Trader;
            Trader = null;
            Logger.Log($"[Trade] Trading with {trader.Instance.GetName(NpcNameSlot.Slot0)} ended", LogCat.Dialog);
            GlobalEventDispatcher.TradeEnded.Invoke(trader);

            if (isDialogResumed && _gameStateService.Dialogs.IsInDialog && trader.Props != null)
                trader.Props.AnimationQueue.Enqueue(new StartProcessInfos(
                    new AnimationAction(int0: _returnInformation), trader));
        }

        /// <summary>
        /// What the NPC can trade: its inventory without equipped items and the currency.
        /// </summary>
        public List<ContentItem> GetGoods(NpcContainer npc)
        {
            var currency = CurrencyName;
            var equipped = npc.Props.EquippedItems.Select(i => i.Index).ToHashSet();
            return _npcInventoryService.GetAllInventoryItems(npc.Instance)
                .Where(i => !i.Name.EqualsIgnoreCase(currency))
                .Where(i => _vmCacheService.TryGetItemData(i.Name) is { } data && !equipped.Contains(data.Index))
                .ToList();
        }

        public int GetCurrencyAmount(NpcContainer npc)
        {
            var currency = CurrencyName;
            return _npcInventoryService.GetAllInventoryItems(npc.Instance)
                .Where(i => i.Name.EqualsIgnoreCase(currency))
                .Sum(i => i.Amount);
        }

        /// <summary>
        /// What the hero pays for one: the item's value.
        /// </summary>
        public int GetBuyPrice(string itemName)
        {
            return _vmCacheService.TryGetItemData(itemName)?.Value ?? 0;
        }

        /// <summary>
        /// What the hero gets for one: the item's value times TRADE_VALUE_MULTIPLIER (G2), rounded up.
        /// </summary>
        public int GetSellPrice(string itemName)
        {
            ReadScriptValues();
            return Mathf.CeilToInt(_sellMultiplier * GetBuyPrice(itemName));
        }

        public bool CanBuy(string itemName, int amount = 1)
        {
            var hero = GetHero();
            return IsTrading && hero != null && GetBuyPrice(itemName) * amount <= GetCurrencyAmount(hero);
        }

        /// <summary>
        /// The hero buys from the trader: the item and its price change hands.
        /// </summary>
        public bool Buy(string itemName, int amount = 1)
        {
            var hero = GetHero();
            var item = _vmCacheService.TryGetItemData(itemName);
            if (item == null || !CanBuy(itemName, amount) || !HasGoods(Trader, itemName, amount))
                return false;

            var price = GetBuyPrice(itemName) * amount;
            _npcInventoryService.ExtRemoveInvItems(Trader.Instance, item.Index, amount);
            _npcInventoryService.ExtCreateInvItems(hero.Instance, item.Index, amount);
            if (price > 0)
                _npcInventoryService.ExtRemoveInvItems(hero.Instance, GetCurrencyIndex(), price);

            Logger.Log($"[Trade] Bought {amount}x {itemName} for {price}", LogCat.Dialog);
            return true;
        }

        /// <summary>
        /// The hero sells to the trader: the item goes to him, the hero gets its sell price.
        /// </summary>
        public bool Sell(string itemName, int amount = 1)
        {
            var hero = GetHero();
            var item = _vmCacheService.TryGetItemData(itemName);
            if (!IsTrading || hero == null || item == null || itemName.EqualsIgnoreCase(CurrencyName) ||
                !HasGoods(hero, itemName, amount))
                return false;

            var price = GetSellPrice(itemName) * amount;
            _npcInventoryService.ExtRemoveInvItems(hero.Instance, item.Index, amount);
            _npcInventoryService.ExtCreateInvItems(Trader.Instance, item.Index, amount);
            if (price > 0)
                _npcInventoryService.ExtCreateInvItems(hero.Instance, GetCurrencyIndex(), price);

            Logger.Log($"[Trade] Sold {amount}x {itemName} for {price}", LogCat.Dialog);
            return true;
        }

        private bool HasGoods(NpcContainer npc, string itemName, int amount)
        {
            return GetGoods(npc).Where(i => i.Name.EqualsIgnoreCase(itemName)).Sum(i => i.Amount) >= amount;
        }

        private NpcContainer GetHero()
        {
            return (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();
        }

        private int GetCurrencyIndex()
        {
            return _vmCacheService.TryGetItemData(CurrencyName).Index;
        }

        /// <summary>
        /// Script constants, read once per game (a load brings a new VM).
        /// </summary>
        private void ReadScriptValues()
        {
            var vm = _gameStateService.GothicVm;
            if (vm == null || _currencyVm == vm)
                return;

            _currencyVm = vm;
            _currencyName = _g1Currency;
            _sellMultiplier = 1f;
            if (!_contextGameVersionService.IsGothic2())
                return;

            var currency = vm.GetSymbolByName("TRADE_CURRENCY_INSTANCE")?.GetString(0);
            if (!string.IsNullOrEmpty(currency))
                _currencyName = currency.ToUpperInvariant();

            var multiplier = vm.GetSymbolByName("TRADE_VALUE_MULTIPLIER");
            if (multiplier != null)
                _sellMultiplier = multiplier.GetFloat(0);
        }
    }
}
