using System.Linq;
using Gothic.Core.Domain.Inventory;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Inventory;
using Gothic.Core.Models.Vm;

namespace Gothic.Core.Services.Inventory
{
    /// <summary>
    /// Hands out the inventory owners the views (VR backpack, NPC loot, trade) show and change, and moves items
    /// between them. Every owner raises GlobalEventDispatcher.NpcInventoryChanged on a change - the views refresh
    /// from that.
    /// </summary>
    public class InventoryTransferService
    {
        private PlayerInventoryOwner _heroOwner;

        /// <summary>
        /// The hero's inventory. One instance - it reads the current hero (PlayerService.HeroContainer) on each call.
        /// </summary>
        public IInventoryOwner GetHeroOwner()
        {
            return _heroOwner ??= new PlayerInventoryOwner();
        }

        /// <summary>
        /// An NPC's inventory. hiddenItem/isEquippedHidden: a trader's goods - without his currency and anything he
        /// wears or wields (the engine's T_Trade view).
        /// </summary>
        public IInventoryOwner GetOwner(NpcContainer npc, string hiddenItem = null, bool isEquippedHidden = false)
        {
            return new NpcInventoryOwner(npc, hiddenItem, isEquippedHidden);
        }

        /// <summary>
        /// Everything the owner (NPC, chest) gives away goes into the hero's inventory - the loot's green X.
        /// Returns the number of items taken.
        /// </summary>
        public int TakeAll(IInventoryOwner from)
        {
            return TakeAll(from, GetHeroOwner());
        }

        /// <summary>
        /// Everything 'from' gives away (IInventoryOwner.CanTake) goes to 'to'. Returns the number of items moved.
        /// </summary>
        public int TakeAll(IInventoryOwner from, IInventoryOwner to)
        {
            var itemCount = 0;
            for (var category = VmGothicEnums.InvCats.InvWeapon; category < VmGothicEnums.InvCats.InvCatMax; category++)
            {
                foreach (var item in from.GetInventory(category).ToList())
                {
                    if (item.Amount <= 0 || !from.CanTake(item.Name))
                        continue;
                    from.Remove(item.Name, item.Amount);
                    to.Add(item.Name, item.Amount);
                    itemCount += item.Amount;
                }
            }
            return itemCount;
        }
    }
}
