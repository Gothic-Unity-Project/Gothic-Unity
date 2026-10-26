using System.Linq;
using Gothic.Core.Domain.Inventory;
using Gothic.Core.Domain.Trade;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Trade;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Trade
{
    /// <summary>
    /// DeveloperConfig.EnableVrTrade: trading with an NPC after a dialog choice with trade != 0. Items laid into the
    /// offers are reserved (out of their owner's inventory) and only change hands on TryCommit(); Cancel() gives
    /// everything back. The difference is paid in currency like OpenGothic (G2 gold, G1 ore) - the trader doesn't run
    /// out of it.
    /// </summary>
    public class TradeService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ConfigService _configService;

        private const string _g1Currency = "ItMiNugget";
        private const string _notEnoughCurrencyFunction = "PLAYER_TRADE_NOT_ENOUGH_GOLD";

        public TradeSession Current { get; private set; }

        public bool IsTrading => Current != null;

        public bool TryStart(NpcContainer trader)
        {
            if (!_configService.Dev.EnableVrTrade || trader == null)
                return false;

            if (Current != null)
            {
                if (Current.Trader == trader)
                    return true;
                Cancel();
            }

            var vm = _gameStateService.GothicVm;
            var currency = vm.GetSymbolByName("TRADE_CURRENCY_INSTANCE")?.GetString(0);
            if (string.IsNullOrEmpty(currency))
                currency = _g1Currency;
            var multiplierSymbol = vm.GetSymbolByName("TRADE_VALUE_MULTIPLIER");
            var sellMultiplier = multiplierSymbol != null ? multiplierSymbol.GetFloat(0) : 1f;

            Current = new TradeSession(trader, new PlayerInventoryOwner(),
                new NpcInventoryOwner(trader, hiddenItem: currency, isEquippedHidden: true), currency, sellMultiplier);

            Logger.Log($"[Trade] Started with {trader.Instance.GetName(NpcNameSlot.Slot0)} (currency {currency}, " +
                       $"sell x{sellMultiplier})", LogCat.Dialog);
            GlobalEventDispatcher.TradeStarted.Invoke(Current);
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

        public int GetPlayerCurrency()
        {
            if (Current == null)
                return 0;
            var currencyItem = _vmCacheService.TryGetItemData(Current.CurrencyInstance);
            if (currencyItem == null)
                return 0;
            var category = ((VmGothicEnums.ItemFlags)currencyItem.MainFlag).ToInventoryCategory();
            return Current.Player.GetInventory(category)
                .Where(i => i.Name.EqualsIgnoreCase(Current.CurrencyInstance))
                .Sum(i => i.Amount);
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
        /// Swaps the offers and settles the difference in currency. False (and the script's message) when the hero
        /// can't pay.
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
        /// Ends the trade, the offers go back to their owners (closing, walking away, fight, saving).
        /// </summary>
        public void Cancel()
        {
            if (Current == null)
                return;

            var session = Current;
            Current = null;

            foreach (var item in session.PlayerOffer.Items)
                session.Player.Add(item.Name, item.Amount);
            foreach (var item in session.TraderOffer.Items)
                session.TraderGoods.Add(item.Name, item.Amount);
            session.PlayerOffer.Clear();
            session.TraderOffer.Clear();

            Logger.Log($"[Trade] Closed with {session.Trader.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Dialog);
            GlobalEventDispatcher.TradeClosed.Invoke(session);
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
    }
}
