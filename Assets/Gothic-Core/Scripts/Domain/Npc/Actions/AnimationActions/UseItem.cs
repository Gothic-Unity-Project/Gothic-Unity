using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using ZenKit.Daedalus;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_UseItem: like the engine, the NPC uses an item it carries - its use function runs (a potion heals, food
    /// feeds) and food and potions are used up. NPCs drink their potions after a fight this way (ZS_HealSelf).
    /// </summary>
    public class UseItem : AbstractAnimationAction
    {
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VmCacheService _vmCacheService;

        public UseItem(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            base.Start();
            IsFinishedFlag = true;

            var item = _vmCacheService.TryGetItemData(Action.Int0);
            if (item == null || _npcInventoryService.ExtNpcHasItems(NpcInstance, item.Index) <= 0)
                return;

            _npcInventoryService.UseItem(NpcContainer, item);

            var category = (VmGothicEnums.ItemFlags)item.MainFlag;
            if (category is VmGothicEnums.ItemFlags.ItemKatFood or VmGothicEnums.ItemFlags.ItemKatPotions)
                _npcInventoryService.ExtRemoveInvItems(NpcInstance, item.Index, 1);
        }
    }
}
