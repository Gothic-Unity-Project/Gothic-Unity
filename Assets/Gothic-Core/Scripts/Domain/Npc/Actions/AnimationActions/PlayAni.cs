using GUZ.Core.Models.Container;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class PlayAni : AbstractAnimationAction
    {
        private string _animName => Action.String0;

        public PlayAni(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var animFound = PrefabProps.AnimationSystem.PlayAnimation(_animName);

            if (!animFound)
            {
                IsFinishedFlag = true;
                return;
            }
            ActionEndEventTime = PrefabProps.AnimationSystem.GetAnimationDuration(_animName);
        }
    }
}
