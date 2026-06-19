using Gothic.Core.Models.Container;
using Gothic.Core.Extensions;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class GoToNpc : AbstractWalkAnimationAction2
    {
        private const float ConversationDistance = 1.5f;

        private Transform _destinationTransform;

        public GoToNpc(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            _destinationTransform = Action.Instance0.GetUserData().Go.transform;

            base.Start();
        }

        protected override Vector3 GetWalkDestination()
        {
            var targetPos = _destinationTransform.position;
            var toTarget = targetPos - NpcGo.transform.position;
            if (toTarget.sqrMagnitude < 0.001f)
                return targetPos;
            return targetPos + toTarget.normalized * -ConversationDistance;
        }

        protected override void OnDestinationReached()
        {
            base.OnDestinationReached();
            IsFinishedFlag = true;
        }
    }
}
