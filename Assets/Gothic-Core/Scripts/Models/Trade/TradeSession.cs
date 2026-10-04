using Gothic.Core.Models.Container;
using Gothic.Core.Models.Inventory;

namespace Gothic.Core.Models.Trade
{
    /// <summary>
    /// A running trade between the hero and a trader (C_Info with trade != 0).
    /// </summary>
    public class TradeSession
    {
        public NpcContainer Trader { get; }

        public IInventoryOwner Player { get; }

        /// <summary>
        /// The trader's inventory without his equipped items and his currency (like the engine's trade view).
        /// </summary>
        public IInventoryOwner TraderGoods { get; }

        /// <summary>
        /// What the hero gives (sells).
        /// </summary>
        public TradeOffer PlayerOffer { get; } = new();

        /// <summary>
        /// What the hero takes (buys).
        /// </summary>
        public TradeOffer TraderOffer { get; } = new();

        /// <summary>
        /// G2: TRADE_CURRENCY_INSTANCE (ItMi_Gold), G1: ItMiNugget (ore).
        /// </summary>
        public string CurrencyInstance { get; }

        /// <summary>
        /// Sell price factor: G2 TRADE_VALUE_MULTIPLIER, G1 1.0.
        /// </summary>
        public float SellMultiplier { get; }

        public TradeSession(NpcContainer trader, IInventoryOwner player, IInventoryOwner traderGoods,
            string currencyInstance, float sellMultiplier)
        {
            Trader = trader;
            Player = player;
            TraderGoods = traderGoods;
            CurrencyInstance = currencyInstance;
            SellMultiplier = sellMultiplier;
        }
    }
}
