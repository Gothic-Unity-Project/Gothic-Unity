using Gothic.Core.Models.Container;
using Gothic.Core.Extensions;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class GoToNpc : AbstractWalkAnimationAction
    {
        private Transform _destinationTransform;

        public GoToNpc(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            base.Start();

            _destinationTransform = Action.Instance0.GetUserData().Go.transform;
        }

        protected override Vector3 GetWalkDestination()
        {
            return _destinationTransform.position;
        }


        protected override void AnimationEnd()
        {
            base.AnimationEnd();

            IsFinishedFlag = false;
        }

        protected override void OnDestinationReached()
        {
            base.OnDestinationReached();

            AnimationEnd();

            State = WalkState.Done;
            IsFinishedFlag = true;
        }
    }
}
