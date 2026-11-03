using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Domain.Inventory;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Domain.Trade;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Trade;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Player
{
    /// <summary>
    /// Trading with an NPC, like the engine (OpenGothic Npc::buyItem/sellItem): a dialog entry with C_Info.trade opens
    /// it after the NPC's lines, and the dialog's choices come back once it ends. The hero pays the item's value, and
    /// gets the value times TRADE_VALUE_MULTIPLIER (G2) for what he sells. The trader's own money doesn't change, as in
    /// the engine. Neither side trades equipped items or the currency.
    /// Two ways to trade on the same rules:
    /// - piece by piece (Buy/Sell): each item changes hands at once (the VR trade window, Flat later),
    /// - DeveloperConfig.EnableVrTrade, the VR trade counter (Current): items laid into the offers are reserved (out of
    ///   their owner's inventory) and only change hands on TryCommit(); the difference is paid in currency. Ending the
    ///   trade gives the offers back. The dialog goes on meanwhile (its choices come back at once, like talking to a
    ///   cashier) and its end ends the trade.
    /// </summary>
    public class TradeService
    {
        // G1 trades with ore. G2 names its currency in TRADE_CURRENCY_INSTANCE.
        private const string _g1Currency = "ITMINUGGET";
        private const string _notEnoughCurrencyFunction = "PLAYER_TRADE_NOT_ENOUGH_GOLD";

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly ConfigService _configService;

        private int _returnInformation;
        private DaedalusVm _currencyVm;
        private string _currencyName;
        private float _sellMultiplier = 1f;

        /// <summary>
        /// The currency the player holds in his hands (set by VR) - it counts in the inventory, but paying with it left
        /// the stack in the hand (the gold was there twice after letting go).
        /// </summary>
        public Func<string, int> GetHeldAmount;

        public TradeService()
        {
            // Leaving the world (level change, loading a save): the offers go back, no dialog to return to.
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(() => End(false));
        }

        public NpcContainer Trader { get; private set; }

        public bool IsTrading => Trader != null;

        /// <summary>
        /// DeveloperConfig.EnableVrTrade: the offers of the VR trade counter, null for the piece by piece trade.
        /// </summary>
        public TradeSession Current { get; private set; }

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
            ReadScriptValues();
            if (_configService.Dev.EnableVrTrade)
                Current = new TradeSession(trader, new PlayerInventoryOwner(),
                    new NpcInventoryOwner(trader, hiddenItem: _currencyName, isEquippedHidden: true), _currencyName,
                    _sellMultiplier);

            Logger.Log($"[Trade] Trading with {trader.Instance.GetName(NpcNameSlot.Slot0)} (currency {_currencyName}, " +
                       $"sell x{_sellMultiplier}{(Current != null ? ", counter" : "")})", LogCat.Dialog);
            GlobalEventDispatcher.TradeStarted.Invoke(trader);

            // The counter doesn't hold the dialog: its choices come back right away.
            if (Current != null)
                ResumeDialog(trader);
        }

        /// <param name="isDialogResumed">The dialog goes on with its choices (the player closed the trade), not when
        /// the dialog itself ended or the world is left.</param>
        public void End(bool isDialogResumed = true)
        {
            if (!IsTrading)
                return;

            var trader = Trader;
            // The counter's dialog went on already.
            var isDialogWaiting = Current == null;
            ReturnOffers();
            Trader = null;
            Current = null;
            Logger.Log($"[Trade] Trading with {trader.Instance.GetName(NpcNameSlot.Slot0)} ended", LogCat.Dialog);
            GlobalEventDispatcher.TradeEnded.Invoke(trader);

            if (isDialogResumed && isDialogWaiting)
                ResumeDialog(trader);
        }

        /// <summary>
        /// The dialog's choices of the entry that opened the trade.
        /// </summary>
        private void ResumeDialog(NpcContainer trader)
        {
            if (_gameStateService.Dialogs.IsInDialog && trader.Props != null)
                trader.Props.AnimationQueue.Enqueue(new StartProcessInfos(
                    new AnimationAction(int0: _returnInformation), trader));
        }

        /// <summary>
        /// What the NPC can trade: its inventory without equipped items and the currency.
        /// </summary>
        public List<ContentItem> GetGoods(NpcContainer npc)
        {
            if (npc.Props == null)
            {
                Logger.LogWarning($"[Trade] {npc.Instance.GetName(NpcNameSlot.Slot0)} has no props - no goods",
                    LogCat.Dialog);
                return new List<ContentItem>();
            }

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
            return TradePricing.BuyPrice(GetValue(itemName));
        }

        /// <summary>
        /// What the hero gets for one: the item's value times TRADE_VALUE_MULTIPLIER (G2), rounded up.
        /// </summary>
        public int GetSellPrice(string itemName)
        {
            ReadScriptValues();
            return TradePricing.SellPrice(GetValue(itemName), _sellMultiplier);
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
            if (!TryGetCurrencyIndex(out var currencyIndex))
                return false;

            var price = GetBuyPrice(itemName) * amount;
            _npcInventoryService.ExtRemoveInvItems(Trader.Instance, item.Index, amount);
            _npcInventoryService.ExtCreateInvItems(hero.Instance, item.Index, amount);
            if (price > 0)
                _npcInventoryService.ExtRemoveInvItems(hero.Instance, currencyIndex, price);

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
            if (!TryGetCurrencyIndex(out var currencyIndex))
                return false;

            var price = GetSellPrice(itemName) * amount;
            _npcInventoryService.ExtRemoveInvItems(hero.Instance, item.Index, amount);
            _npcInventoryService.ExtCreateInvItems(Trader.Instance, item.Index, amount);
            if (price > 0)
                _npcInventoryService.ExtCreateInvItems(hero.Instance, currencyIndex, price);

            Logger.Log($"[Trade] Sold {amount}x {itemName} for {price}", LogCat.Dialog);
            return true;
        }

        /// <summary>
        /// The hero laid one of his items on his side (it already left his inventory).
        /// </summary>
        public void OfferFromPlayer(string itemInstanceName, int amount)
        {
            if (Current == null)
                return;
            Current.PlayerOffer.Add(itemInstanceName, amount);
            GlobalEventDispatcher.TradeOfferChanged.Invoke(Current);
        }

        public void WithdrawFromPlayerOffer(string itemInstanceName, int amount)
        {
            if (Current == null)
                return;
            Current.PlayerOffer.Remove(itemInstanceName, amount);
            GlobalEventDispatcher.TradeOfferChanged.Invoke(Current);
        }

        /// <summary>
        /// The hero laid one of the trader's goods on the trader's side (it already left the trader's inventory).
        /// </summary>
        public void OfferFromTrader(string itemInstanceName, int amount)
        {
            if (Current == null)
                return;
            Current.TraderOffer.Add(itemInstanceName, amount);
            GlobalEventDispatcher.TradeOfferChanged.Invoke(Current);
        }

        public void WithdrawFromTraderOffer(string itemInstanceName, int amount)
        {
            if (Current == null)
                return;
            Current.TraderOffer.Remove(itemInstanceName, amount);
            GlobalEventDispatcher.TradeOfferChanged.Invoke(Current);
        }

        public int GetValue(string itemInstanceName)
        {
            return _vmCacheService.TryGetItemData(itemInstanceName)?.Value ?? 0;
        }

        /// <summary>
        /// Positive: the hero pays. Negative: the hero gets currency.
        /// </summary>
        public int GetBalance()
        {
            return Current == null ? 0 : TradePricing.Balance(Current, GetValue);
        }

        public int GetPlayerOfferValue()
        {
            return Current == null ? 0 : TradePricing.PlayerOfferValue(Current, GetValue);
        }

        public int GetTraderOfferValue()
        {
            return Current == null ? 0 : TradePricing.TraderOfferValue(Current, GetValue);
        }

        /// <summary>
        /// The counter's currency: what the hero owns without what his hands hold.
        /// </summary>
        public int GetPlayerCurrency()
        {
            if (Current == null)
                return 0;
            var currencyItem = _vmCacheService.TryGetItemData(Current.CurrencyInstance);
            if (currencyItem == null)
                return 0;
            var category = ((VmGothicEnums.ItemFlags)currencyItem.MainFlag).ToInventoryCategory();
            var owned = Current.Player.GetInventory(category)
                .Where(i => i.Name.EqualsIgnoreCase(Current.CurrencyInstance))
                .Sum(i => i.Amount);
            return Math.Max(0, owned - (GetHeldAmount?.Invoke(Current.CurrencyInstance) ?? 0));
        }

        public string GetCurrencyDisplayName()
        {
            return Current == null ? "" : _vmCacheService.TryGetItemData(Current.CurrencyInstance)?.Name ?? "";
        }

        public bool CanCommit()
        {
            if (Current == null || (Current.PlayerOffer.IsEmpty && Current.TraderOffer.IsEmpty))
                return false;
            return GetBalance() <= GetPlayerCurrency();
        }

        /// <summary>
        /// The counter's offers change hands and the difference is paid in currency. False (and the script's message)
        /// when the hero can't pay.
        /// </summary>
        public bool TryCommit()
        {
            if (Current == null)
                return false;

            var balance = GetBalance();
            if (balance > GetPlayerCurrency())
            {
                ShowNotEnoughCurrency();
                return false;
            }
            if (Current.PlayerOffer.IsEmpty && Current.TraderOffer.IsEmpty)
                return false;

            foreach (var item in Current.PlayerOffer.Items)
                Current.TraderGoods.Add(item.Name, item.Amount);
            foreach (var item in Current.TraderOffer.Items)
                Current.Player.Add(item.Name, item.Amount);

            if (balance > 0)
            {
                Current.Player.Remove(Current.CurrencyInstance, balance);
                Current.TraderGoods.Add(Current.CurrencyInstance, balance);
            }
            else if (balance < 0)
            {
                Current.Player.Add(Current.CurrencyInstance, -balance);
            }

            Logger.Log($"[Trade] Committed with {Current.Trader.Instance.GetName(NpcNameSlot.Slot0)}: " +
                       $"{Current.PlayerOffer.Items.Count} sold, {Current.TraderOffer.Items.Count} bought, " +
                       $"balance {balance} {Current.CurrencyInstance}", LogCat.Dialog);

            Current.PlayerOffer.Clear();
            Current.TraderOffer.Clear();
            GlobalEventDispatcher.TradeCommitted.Invoke(Current);
            GlobalEventDispatcher.TradeOfferChanged.Invoke(Current);
            return true;
        }

        /// <summary>
        /// The counter's offers go back to their owners (the trade ends).
        /// </summary>
        private void ReturnOffers()
        {
            if (Current == null)
                return;

            foreach (var item in Current.PlayerOffer.Items)
                Current.Player.Add(item.Name, item.Amount);
            foreach (var item in Current.TraderOffer.Items)
                Current.TraderGoods.Add(item.Name, item.Amount);
            Current.PlayerOffer.Clear();
            Current.TraderOffer.Clear();
        }

        /// <summary>
        /// G2: Print(PRINT_Trade_Not_Enough_Gold) via the script.
        /// </summary>
        private void ShowNotEnoughCurrency()
        {
            var vm = _gameStateService.GothicVm;
            if (vm.GetSymbolByName(_notEnoughCurrencyFunction) == null)
            {
                Logger.Log("[Trade] Not enough currency", LogCat.Dialog);
                return;
            }

            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = vm.GlobalHero;
            try
            {
                vm.Call(_notEnoughCurrencyFunction);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }
        }

        private bool HasGoods(NpcContainer npc, string itemName, int amount)
        {
            return GetGoods(npc).Where(i => i.Name.EqualsIgnoreCase(itemName)).Sum(i => i.Amount) >= amount;
        }

        private NpcContainer GetHero()
        {
            return (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();
        }

        /// <summary>
        /// Resolved before any item changes hands, so a missing currency item (a mod, a wrong TRADE_CURRENCY_INSTANCE)
        /// stops the trade instead of leaving it half done.
        /// </summary>
        private bool TryGetCurrencyIndex(out int index)
        {
            var currency = _vmCacheService.TryGetItemData(CurrencyName);
            index = currency?.Index ?? -1;
            if (currency == null)
                Logger.LogWarning($"[Trade] Currency item {CurrencyName} doesn't exist - no trade", LogCat.Dialog);
            return currency != null;
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
