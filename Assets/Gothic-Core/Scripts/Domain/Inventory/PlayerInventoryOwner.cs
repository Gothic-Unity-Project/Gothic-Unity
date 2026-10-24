using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Inventory;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Reflex.Attributes;

namespace Gothic.Core.Domain.Inventory
{
    /// <summary>
    /// The hero's inventory (VR backpack). Items held in the VR hands count as inventory too (VRPlayerService).
    /// </summary>
    public class PlayerInventoryOwner : IInventoryOwner
    {
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VmCacheService _vmCacheService;

        public PlayerInventoryOwner()
        {
            this.Inject();
        }

        public NpcContainer Npc => _playerService.HeroContainer;

        public bool IsHero => true;

        public List<ContentItem> GetInventory(VmGothicEnums.InvCats category)
        {
            return _playerService.GetInventory(category);
        }

        public void Add(string itemInstanceName, int amount)
        {
            _playerService.AddItem(itemInstanceName, amount);
        }

        public void Remove(string itemInstanceName, int amount)
        {
            _playerService.RemoveItem(itemInstanceName, amount);
            UnequipIfLastOneTakenOut(itemInstanceName);
        }

        public bool IsEquipped(string itemInstanceName)
        {
            return _npcInventoryService.IsEquipped(Npc.Instance, itemInstanceName);
        }

        public bool CanTake(string itemInstanceName)
        {
            return true;
        }

        /// <summary>
        /// Equipped items live in the backpack. Taking the last one of them out means taking it off.
        /// </summary>
        private void UnequipIfLastOneTakenOut(string itemInstanceName)
        {
            var item = _vmCacheService.TryGetItemData(itemInstanceName);
            if (item == null)
                return;

            var category = ((VmGothicEnums.ItemFlags)item.MainFlag).ToInventoryCategory();
            var isStillOwned = GetInventory(category)
                .Any(i => i.Name.EqualsIgnoreCase(itemInstanceName) && i.Amount > 0);
            if (!isStillOwned)
                _npcInventoryService.UnequipItemWithEffects(Npc.Instance, item);
        }
    }
}
