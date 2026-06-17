using Gothic.Core.Models.Container;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
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

        public override void Tick()
        {
            base.Tick();

            if (IsFinishedFlag)
                return;

            // An external stop (e.g. AI_StopAni) triggered the track's blend-out before
            // the natural duration elapsed — finish the action now rather than waiting
            // for the timer.
            if (PrefabProps.AnimationSystem.IsAnimationBlendingOut(_animName))
            {
                AnimationEnd();
            }
        }
    }
}
