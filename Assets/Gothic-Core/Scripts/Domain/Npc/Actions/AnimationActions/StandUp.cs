using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class StandUp : AbstractAnimationAction
    {
        [Inject] private readonly NpcService _npcService;

        public StandUp(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            // Ends the mob usage. After AI_StandUp's back transitions the NPC already stands; AI_StandUpQuick pops.
            if (PrefabProps.CurrentInteractable != null)
            {
                PrefabProps.CurrentInteractable = null;
                PrefabProps.CurrentInteractableSlot = null;
                Props.CurrentInteractableStateId = -1;
                Props.BodyState = VmGothicEnums.BodyState.BsStand;
            }

            // Like the engine's AI_StandUp, an item in the hand (food, beer) is put away - B_FullStop on aggro
            // left it there.
            if (Props.BodyState != VmGothicEnums.BodyState.BsDead)
                _npcService.RemoveUsedItemFromHand(NpcContainer);

            // Re-enable physics in case it was disabled by a fall (e.g. unconscious recovery).
            PhysicsService.EnablePhysicsForNpc(PrefabProps);

            // Playing the idle blends out a possibly running Mobsi loop animation on the same layer.
            PrefabProps.AnimationSystem.PlayIdleAnimation();
        }

        public override bool IsFinished()
        {
            return true;
        }

        public override bool HasEnded => true;
    }
}
