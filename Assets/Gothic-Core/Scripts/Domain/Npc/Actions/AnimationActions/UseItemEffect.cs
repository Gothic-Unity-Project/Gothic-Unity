using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
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

            var vm = GameStateService.GothicVm;
            var useFunction = item.GetOnState(0);
            if (useFunction <= 0)
                return;

            var oldSelf = vm.GlobalSelf;
            var oldItem = vm.GlobalItem;
            vm.GlobalSelf = NpcInstance;
            vm.GlobalItem = item;
            try
            {
                vm.Call(useFunction);
                Logger.Log($"[AI_UseItem] {NpcInstance.GetName(NpcNameSlot.Slot0)} used {item.Name}", LogCat.Ai);
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"[AI_UseItem] {item.Name} use function failed: {e.Message}", LogCat.Ai);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalItem = oldItem;
            }
        }
    }
}
