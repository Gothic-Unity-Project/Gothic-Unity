using Gothic.Core.Models.Container;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_StopPointAt: the pointing arm goes down.
    /// </summary>
    public class StopPointAt : AbstractAnimationAction
    {
        public StopPointAt(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            base.Start();

            PrefabProps.AnimationSystem.StopAnimation(PointAt.PointAnimation);
            IsFinishedFlag = true;
        }
    }
}
