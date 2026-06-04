using Gothic.Core.Models.Container;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class StandUp : AbstractAnimationAction
    {
        public StandUp(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            PrefabProps.AnimationSystem.PlayIdleAnimation();
        }

        public override bool IsFinished()
        {
            return true;
        }
    }
}
