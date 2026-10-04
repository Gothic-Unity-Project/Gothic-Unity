using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Vob;

namespace Gothic.Core.Models.Trade
{
    /// <summary>
    /// One side of a trade: the items laid on the counter. They are reserved - out of their owner's inventory, but
    /// not transferred before TradeService.TryCommit().
    /// </summary>
    public class TradeOffer
    {
        private readonly List<ContentItem> _items = new();

        public IReadOnlyList<ContentItem> Items => _items;

        public bool IsEmpty => _items.Count == 0;

        public void Add(string itemInstanceName, int amount)
        {
            var item = _items.FirstOrDefault(i => i.Name.EqualsIgnoreCase(itemInstanceName));
            if (item != null)
                item.Amount += amount;
            else
                _items.Add(new ContentItem(itemInstanceName, amount));
        }

        public void Remove(string itemInstanceName, int amount)
        {
            var item = _items.FirstOrDefault(i => i.Name.EqualsIgnoreCase(itemInstanceName));
            if (item == null)
                return;

            item.Amount -= amount;
            if (item.Amount <= 0)
                _items.Remove(item);
        }

        public void Clear()
        {
            _items.Clear();
        }
    }
}
