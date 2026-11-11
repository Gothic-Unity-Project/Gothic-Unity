using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Inventory;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Reflex.Attributes;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Inventory
{
    /// <summary>
    /// The items of an oCMobContainer (its "contents" string), e.g. a mummy you loot without a lid to open.
    /// Changes go straight into IContainer.Contents, so they are saved with the world (WORLD.SAV).
    /// </summary>
    public class ContainerInventoryOwner : IInventoryOwner
    {
        [Inject] private readonly VmCacheService _vmCacheService;

        private readonly IContainer _container;

        public ContainerInventoryOwner(IContainer container)
        {
            _container = container;
            this.Inject();
        }

        public NpcContainer Npc => null;

        public bool IsHero => false;

        public List<ContentItem> GetInventory(VmGothicEnums.InvCats category)
        {
            return GetItems()
                .Where(i => GetCategory(i.Name) == category)
                .ToList();
        }

        public void Add(string itemInstanceName, int amount)
        {
            if (_vmCacheService.TryGetItemData(itemInstanceName) == null)
            {
                Logger.LogWarning($"[ContainerInventoryOwner] Unknown item {itemInstanceName} - not added.", LogCat.Vob);
                return;
            }

            var items = GetItems();
            var existing = items.FirstOrDefault(i => i.Name.EqualsIgnoreCase(itemInstanceName));
            if (existing != null)
                existing.Amount += amount;
            else
                items.Add(new ContentItem(itemInstanceName, amount));

            SetItems(items);
        }

        public void Remove(string itemInstanceName, int amount)
        {
            var items = GetItems();
            var existing = items.FirstOrDefault(i => i.Name.EqualsIgnoreCase(itemInstanceName));
            if (existing == null)
            {
                Logger.LogWarning($"[ContainerInventoryOwner] {itemInstanceName} not in {_container.Name} - not removed.",
                    LogCat.Vob);
                return;
            }

            existing.Amount -= amount;
            if (existing.Amount <= 0)
                items.Remove(existing);

            SetItems(items);
        }

        public bool IsEquipped(string itemInstanceName) => false;

        public bool CanTake(string itemInstanceName) => _vmCacheService.TryGetItemData(itemInstanceName) != null;

        private List<ContentItem> GetItems()
        {
            try
            {
                return _container.Contents.UnpackItems()
                    .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                    .Select(i => new ContentItem(i.Name.Trim(), i.Amount))
                    .ToList();
            }
            catch (FormatException e)
            {
                // A mod's broken "contents" (ITFO_X:abc) - an empty loot instead of a half built backpack.
                Logger.LogWarning($"[ContainerInventoryOwner] {_container.Name}: contents '{_container.Contents}' " +
                                  $"can't be read ({e.Message}) - shown empty.", LogCat.Vob);
                return new List<ContentItem>();
            }
        }

        private void SetItems(List<ContentItem> items)
        {
            _container.Contents = items.PackItems(',');
        }

        private VmGothicEnums.InvCats GetCategory(string itemInstanceName)
        {
            var item = _vmCacheService.TryGetItemData(itemInstanceName);
            return item == null
                ? VmGothicEnums.InvCats.InvMisc
                : ((VmGothicEnums.ItemFlags)item.MainFlag).ToInventoryCategory();
        }
    }
}
