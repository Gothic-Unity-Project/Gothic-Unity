using Gothic.Core.Models.Container;
using Gothic.Core.Extensions;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class GoToNpc : AbstractWalkAnimationAction
    {
        private const float ConversationDistance = 1.5f;

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
            var targetPos = _destinationTransform.position;
            var toTarget = targetPos - NpcGo.transform.position;
            if (toTarget.sqrMagnitude < 0.001f)
                return targetPos;
            return targetPos + toTarget.normalized * -ConversationDistance;
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
