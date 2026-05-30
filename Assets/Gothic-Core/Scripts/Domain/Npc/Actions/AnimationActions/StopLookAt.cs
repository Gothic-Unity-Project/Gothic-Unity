using GUZ.Core.Models.Container;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class StopLookAtNpc : AbstractAnimationAction
    {
        public StopLookAtNpc(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        { }

        public override void Start()
        {
            PrefabProps.AnimationHeadHandler.StopLookAt();

            IsFinishedFlag = true;
        }
    }
}
