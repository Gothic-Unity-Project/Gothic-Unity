using System;
using System.Linq;
using Gothic.Core.Models.Trade;

namespace Gothic.Core.Domain.Trade
{
    /// <summary>
    /// Prices like the engine (OpenGothic Inventory::priceOf/sellPriceOf): buying costs the item's value, selling
    /// brings ceil(multiplier * value). Pure, no VM - unit testable.
    /// </summary>
    public static class TradePricing
    {
        public static int BuyPrice(int value)
        {
            return Math.Max(0, value);
        }

        public static int SellPrice(int value, float multiplier)
        {
            // Rounded first: the float multiplier 0.15f * 100 is 15.0000006 - ceil made it 16 instead of 15.
            return Math.Max(0, (int)Math.Ceiling(Math.Round((double)multiplier * value, 3)));
        }

        /// <summary>
        /// What the hero buys, in currency.
        /// </summary>
        public static int TraderOfferValue(TradeSession session, Func<string, int> valueOf)
        {
            return session.TraderOffer.Items.Sum(i => BuyPrice(valueOf(i.Name)) * i.Amount);
        }

        /// <summary>
        /// What the hero sells, in currency.
        /// </summary>
        public static int PlayerOfferValue(TradeSession session, Func<string, int> valueOf)
        {
            return session.PlayerOffer.Items.Sum(i => SellPrice(valueOf(i.Name), session.SellMultiplier) * i.Amount);
        }

        /// <summary>
        /// Positive: the hero pays this much currency. Negative: the hero gets it.
        /// </summary>
        public static int Balance(TradeSession session, Func<string, int> valueOf)
        {
            return TraderOfferValue(session, valueOf) - PlayerOfferValue(session, valueOf);
        }
    }
}
