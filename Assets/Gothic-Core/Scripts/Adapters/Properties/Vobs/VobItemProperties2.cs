using System;
using GUZ.Core.Services.Caches;
using Reflex.Attributes;
using ZenKit.Daedalus;
using ZenKit.Vobs;

namespace GUZ.Core.Adapters.Properties.Vobs
{
    public class VobItemProperties2 : VobProperties2
    {
        [Inject] private readonly VmCacheService _vmCacheService;

        public ItemInstance Instance { get; private set; }

        public VobItemProperties2(IVirtualObject vob) : base(vob)
        { }

        public override void Init()
        {
            base.Init();
            
            var vobItem = (IItem)Vob;
            
            string itemName;
            if (!string.IsNullOrEmpty(vobItem.Instance))
                itemName = vobItem.Instance;
            else if (!string.IsNullOrEmpty(vobItem.Name))
                itemName = vobItem.Name;
            else
                throw new Exception("Vob Item -> no usable name found.");

            Instance = _vmCacheService.TryGetItemData(itemName);
        }

        public override string GetFocusName()
        {
            var vobItem = VobAs<IItem>();
            var itemVmData = _vmCacheService.TryGetItemData(vobItem.Name);
            
            if (vobItem?.Amount > 1)
                return $"{itemVmData?.Name} ({vobItem.Amount})";
            else
                return itemVmData?.Name;
        }
    }
}
