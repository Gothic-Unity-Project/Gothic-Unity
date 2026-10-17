using System.Collections.Generic;
using Gothic.Core.Domain.Trade;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Inventory;
using Gothic.Core.Models.Trade;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using NUnit.Framework;

namespace Gothic.Tests.PlayMode
{
    /// <summary>
    /// TradePricing/TradeOffer without VM or scene: engine prices (buy = value, sell = ceil(multiplier * value)) and the
    /// balance the hero pays or gets.
    /// </summary>
    public class TradePricingTests
    {
        private class FakeOwner : IInventoryOwner
        {
            public NpcContainer Npc => null;
            public bool IsHero { get; set; }
            public List<ContentItem> GetInventory(VmGothicEnums.InvCats category) => new();
            public void Add(string itemInstanceName, int amount) { }
            public void Remove(string itemInstanceName, int amount) { }
            public bool IsEquipped(string itemInstanceName) => false;
            public bool CanTake(string itemInstanceName) => true;
        }

        private static readonly Dictionary<string, int> _values = new()
        {
            { "ITMW_SWORD", 100 },
            { "ITFO_APPLE", 7 },
        };

        private static int ValueOf(string itemInstanceName) => _values.GetValueOrDefault(itemInstanceName, 0);

        private static TradeSession CreateSession(float sellMultiplier)
        {
            return new TradeSession(null, new FakeOwner { IsHero = true }, new FakeOwner(), "ITMI_GOLD", sellMultiplier);
        }

        [Test]
        public void SellPrice_IsCeiledMultiplierTimesValue()
        {
            Assert.AreEqual(2, TradePricing.SellPrice(7, 0.15f)); // 1.05 -> 2
            Assert.AreEqual(15, TradePricing.SellPrice(100, 0.15f));
            Assert.AreEqual(100, TradePricing.SellPrice(100, 1f)); // G1
            Assert.AreEqual(0, TradePricing.SellPrice(-5, 1f));
        }

        [Test]
        public void BuyPrice_IsValue()
        {
            Assert.AreEqual(100, TradePricing.BuyPrice(100));
            Assert.AreEqual(0, TradePricing.BuyPrice(-1));
        }

        [Test]
        public void Balance_BuyingCostsTheValue()
        {
            var session = CreateSession(0.15f);
            session.TraderOffer.Add("ITMW_SWORD", 1);
            Assert.AreEqual(100, TradePricing.Balance(session, ValueOf));
        }

        [Test]
        public void Balance_SellingIsNegative_G2Multiplier()
        {
            var session = CreateSession(0.15f);
            session.PlayerOffer.Add("ITFO_APPLE", 10); // 10 * ceil(1.05) = 20
            Assert.AreEqual(-20, TradePricing.Balance(session, ValueOf));
        }

        [Test]
        public void Balance_SwapNetsOut_G1FullValue()
        {
            var session = CreateSession(1f);
            session.PlayerOffer.Add("ITMW_SWORD", 1);
            session.TraderOffer.Add("ITFO_APPLE", 3);
            Assert.AreEqual(21 - 100, TradePricing.Balance(session, ValueOf));
        }

        [Test]
        public void Offer_RemovingEverythingDropsTheEntry()
        {
            var offer = new TradeOffer();
            offer.Add("ITFO_APPLE", 3);
            offer.Add("itfo_apple", 2);
            Assert.AreEqual(5, offer.Items[0].Amount);
            offer.Remove("ITFO_APPLE", 5);
            Assert.IsTrue(offer.IsEmpty);
        }
    }
}
