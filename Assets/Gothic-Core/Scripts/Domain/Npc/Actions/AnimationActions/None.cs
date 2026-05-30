using GUZ.Core.Models.Container;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class None : AbstractAnimationAction
    {
        public None(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            // NOP
        }

        public override bool IsFinished()
        {
            return true;
        }
    }
}
