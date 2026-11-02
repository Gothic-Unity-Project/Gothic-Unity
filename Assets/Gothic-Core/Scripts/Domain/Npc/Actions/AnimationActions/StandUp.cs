using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class StandUp : AbstractAnimationAction
    {
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

            // Re-enable physics in case it was disabled by a fall (e.g. unconscious recovery).
            PhysicsService.EnablePhysicsForNpc(PrefabProps);

            // Playing the idle blends out a possibly running Mobsi loop animation on the same layer.
            PrefabProps.AnimationSystem.PlayIdleAnimation();
        }

        public override bool IsFinished()
        {
            return true;
        }
    }
}
