using System.Collections.Generic;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;

namespace Gothic.Core.Models.Inventory
{
    /// <summary>
    /// Whose items an inventory view (VR backpack, NPC loot backpack, trade counter) shows and changes.
    /// </summary>
    public interface IInventoryOwner
    {
        /// <summary>
        /// The hero or the NPC the items belong to.
        /// </summary>
        NpcContainer Npc { get; }

        bool IsHero { get; }

        List<ContentItem> GetInventory(VmGothicEnums.InvCats category);

        void Add(string itemInstanceName, int amount);

        /// <summary>
        /// Taking the last one of an equipped item out takes it off.
        /// </summary>
        void Remove(string itemInstanceName, int amount);

        bool IsEquipped(string itemInstanceName);

        /// <summary>
        /// False for items the view must not offer (e.g. a trader's currency or worn armor).
        /// </summary>
        bool CanTake(string itemInstanceName);
    }
}
