using System;
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
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Player
{
    /// <summary>
    /// Trading with an NPC, like the engine (OpenGothic Npc::buyItem/sellItem): a dialog entry with C_Info.trade opens
    /// it after the NPC's lines. The hero pays the item's value, and gets the value times TRADE_VALUE_MULTIPLIER (G2) for
    /// what he sells. The trader's own money doesn't change, as in the engine. Neither side trades equipped items or the
    /// currency.
    /// The VR trade counter lays items into the offers (Current): they are reserved (out of their owner's inventory) and
    /// only change hands on TryCommit(); the difference is paid in currency. Ending the trade gives the offers back. The
    /// dialog goes on meanwhile (its choices come back at once, like talking to a cashier) and its end ends the trade.
    /// </summary>
    public class TradeService
    {
        // G1 trades with ore. G2 names its currency in TRADE_CURRENCY_INSTANCE.
        private const string _g1Currency = "ITMINUGGET";
        private const string _notEnoughCurrencyFunction = "PLAYER_TRADE_NOT_ENOUGH_GOLD";

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

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
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(End);
        }

        public NpcContainer Trader { get; private set; }

        public bool IsTrading => Trader != null;

        /// <summary>
        /// The offers of the trade counter, null while nobody trades.
        /// </summary>
        public TradeSession Current { get; private set; }

        /// <param name="returnInformation">The dialog entry that opened it - its choices come back afterwards.</param>
        public void Start(NpcContainer trader, int returnInformation)
        {
            if (IsTrading)
                End();

            _returnInformation = returnInformation;
            try
            {
                Trader = trader;
                ReadScriptValues();
                Current = new TradeSession(trader, new PlayerInventoryOwner(),
                    new NpcInventoryOwner(trader, hiddenItem: _currencyName, isEquippedHidden: true), _currencyName,
                    _sellMultiplier);

                Logger.Log($"[Trade] Trading with {trader.Instance.GetName(NpcNameSlot.Slot0)} " +
                           $"(currency {_currencyName}, sell x{_sellMultiplier})", LogCat.Dialog);
                GlobalEventDispatcher.TradeStarted.Invoke(trader);
            }
            catch (Exception e)
            {
                // No session or no counter: the trade ends, the dialog goes on.
                Logger.LogError($"[Trade] The trade couldn't start: {e}", LogCat.Dialog);
                End();
            }
            finally
            {
                // The counter doesn't hold the dialog: its choices come back right away.
                ResumeDialog(trader);
            }
        }

        /// <summary>
        /// The trade ends (closed, the dialog ended, the world is left): the offers go back to their owners.
        /// </summary>
        public void End()
        {
            if (!IsTrading)
                return;

            var trader = Trader;
            try
            {
                ReturnOffers();
            }
            catch (Exception e)
            {
                Logger.LogError($"[Trade] The offers couldn't go back: {e}", LogCat.Dialog);
            }
            Trader = null;
            Current = null;
            Logger.Log($"[Trade] Trading with {trader.Instance.GetName(NpcNameSlot.Slot0)} ended", LogCat.Dialog);
            try
            {
                GlobalEventDispatcher.TradeEnded.Invoke(trader);
            }
            catch (Exception e)
            {
                // StopDialog ends the trade first - a throwing listener mustn't keep the dialog and the player locked.
                Logger.LogError($"[Trade] A listener of the trade's end failed: {e}", LogCat.Dialog);
            }
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
