using GUZ.Core.Logging;
using GUZ.Core.Models.Container;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class UseItemToState : AbstractAnimationAction
    {
        private const string _animationScheme = "T_{0}_{1}_2_{2}";
        private const string _loopAnimationScheme = "S_{0}_S{1}";

        private int ItemToUse => Action.Int0;
        private int DesiredState => Action.Int1;


        public UseItemToState(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            PlayTransitionAnimation();
        }

        private void PlayTransitionAnimation()
        {
            int current = Props.ItemAnimationState;
            int target = DesiredState;
            int step = (target > current) ? 1 : -1;
            
            var item = VmCacheService.TryGetItemData(ItemToUse);

            while (current != target)
            {
                int next = current + step;

                var oldState = current == -1 ? "STAND" : $"S{current}";
                var newState = next == -1 ? "STAND" : $"S{next}";
                var animationName = string.Format(_animationScheme, item.SchemeName, oldState, newState);

                bool animationFound = PrefabProps.AnimationSystem.PlayAnimation(animationName);

                if (animationFound)
                {
                    Props.ItemAnimationState = next;
                    if (step > 0)
                    {
                        Props.HasItemEquipped = true;
                        Props.CurrentItem = ItemToUse;
                    }
                    else
                    {
                        Props.HasItemEquipped = false;
                        Props.CurrentItem = -1;
                    }
                    ActionEndEventTime = PrefabProps.AnimationSystem.GetAnimationDuration(animationName);
                    return; // Let animation end event continue the transition
                }

                // If we can't play the transition animation, try jumping to the next possible state
                current = next;
            }

            // If we exited the while loop, we couldn't find any animation along the path
            // Just set to target state and log a warning
            Props.ItemAnimationState = target;
            if (step > 0)
            {
                Props.HasItemEquipped = true;
                Props.CurrentItem = ItemToUse;
            }
            else
            {
                Props.HasItemEquipped = false;
                Props.CurrentItem = -1;
            }
            Logger.LogWarningEditor($"No animation found for transition from {Props.ItemAnimationState} to {DesiredState} for item {ItemToUse}. Forced state update.",LogCat.Animation);
        }

        protected override void AnimationEnd()
        {
            base.AnimationEnd();

            if (Props.ItemAnimationState == DesiredState)
            {
                return;
            }

            PlayTransitionAnimation();

            IsFinishedFlag = false;
        }
    }
}
