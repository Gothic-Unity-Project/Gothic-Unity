using System;
using System.Collections.Generic;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Inventory;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;

namespace Gothic.Core.Domain.Inventory
{
    /// <summary>
    /// DeveloperConfig.EnableVrPickpocket: a robbed NPC's pockets - his items without what he wears or wields (the
    /// engine's steal window). Every item taken out is reported (ItemTaken) - each one can get the thief caught.
    /// </summary>
    public class PickpocketInventoryOwner : IInventoryOwner
    {
        private readonly NpcInventoryOwner _pockets;

        public event Action<string, int> ItemTaken;

        public PickpocketInventoryOwner(NpcContainer npc)
        {
            _pockets = new NpcInventoryOwner(npc, isEquippedHidden: true);
        }

        public NpcContainer Npc => _pockets.Npc;

        public bool IsHero => false;

        public List<ContentItem> GetInventory(VmGothicEnums.InvCats category) => _pockets.GetInventory(category);

        public void Add(string itemInstanceName, int amount) => _pockets.Add(itemInstanceName, amount);

        public void Remove(string itemInstanceName, int amount)
        {
            _pockets.Remove(itemInstanceName, amount);
            ItemTaken?.Invoke(itemInstanceName, amount);
        }

        public bool IsEquipped(string itemInstanceName) => _pockets.IsEquipped(itemInstanceName);

        public bool CanTake(string itemInstanceName) => _pockets.CanTake(itemInstanceName);
    }
}
