using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_TakeItem, like the engine (OpenGothic Npc::takeItem): the NPC bends down (S_IGET) and the world item goes into
    /// its inventory. It doesn't walk there - the scripts call AI_GotoItem first when needed. Gone meanwhile (the hero
    /// took it), nothing happens.
    /// </summary>
    public class TakeItem : AbstractAnimationAction
    {
        private static readonly string[] _pickUpAnimations = { "T_STAND_2_IGET", "S_IGET" };

        [Inject] private readonly NpcInventoryService _npcInventoryService;

        private readonly VobContainer _item;
        private string _animation;

        public TakeItem(AnimationAction action, NpcContainer npcContainer, VobContainer item)
            : base(action, npcContainer)
        {
            _item = item;
        }

        public override void Start()
        {
            base.Start();

            if (_item?.Go == null)
            {
                IsFinishedFlag = true;
                return;
            }

            Props.BodyState = VmGothicEnums.BodyState.BsTakeitem;
            foreach (var animation in _pickUpAnimations)
            {
                if (!PrefabProps.AnimationSystem.PlayAnimation(animation))
                    continue;

                _animation = animation;
                ActionEndEventTime = PrefabProps.AnimationSystem.GetAnimationDuration(animation);
                return;
            }

            // Monsters have no such animation.
            PickUp();
            Finish();
        }

        protected override void AnimationEnd()
        {
            PickUp();
            Finish();
        }

        public override void StopImmediately()
        {
            Props.BodyState = VmGothicEnums.BodyState.BsStand;
            base.StopImmediately();
        }

        private void PickUp()
        {
            if (_item?.Go == null || _item.Vob is not IItem vobItem)
                return;

            var instanceName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
            var item = VmCacheService.TryGetItemData(instanceName);
            if (item == null)
                return;

            Logger.Log($"[TakeItem] {NpcInstance.GetName(NpcNameSlot.Slot0)} takes {instanceName}.", LogCat.Npc);
            VobService.RemoveWorldItem(_item);
            _npcInventoryService.ExtCreateInvItems(NpcInstance, item.Index, System.Math.Max(1, vobItem.Amount));
        }

        private void Finish()
        {
            if (_animation != null)
                PrefabProps.AnimationSystem.StopAnimation(_animation);
            Props.BodyState = VmGothicEnums.BodyState.BsStand;
            PrefabProps.AnimationSystem.PlayIdleAnimation();
            IsFinishedFlag = true;
        }
    }
}
