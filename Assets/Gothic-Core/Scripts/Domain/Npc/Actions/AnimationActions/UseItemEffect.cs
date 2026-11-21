using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Inventory;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_UseItem's middle step: the item is used up - one less in the inventory, its on_state[0] function runs with
    /// self = the NPC (a potion heals, food feeds). The item's scheme animations around it come from UseItemToState.
    /// </summary>
    public class UseItemEffect : AbstractAnimationAction
    {
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly ItemUseService _itemUseService;

        private int ItemToUse => Action.Int0;

        public UseItemEffect(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            IsFinishedFlag = true;

            var item = VmCacheService.TryGetItemData(ItemToUse);
            if (item == null || _npcInventoryService.ExtNpcHasItems(NpcInstance, item.Index) <= 0)
                return;

            _npcInventoryService.ExtRemoveInvItems(NpcInstance, item.Index, 1);

            var useFunction = item.GetOnState(0);
            if (useFunction <= 0)
                return;

            // ItemUseService: the one place item functions run (self/item set and restored, ItemFunctionFinished).
            if (_itemUseService.CallItemFunction(NpcInstance, item, useFunction))
                Logger.Log($"[AI_UseItem] {NpcInstance.GetName(NpcNameSlot.Slot0)} used {item.Name}", LogCat.Ai);
        }
    }
}
