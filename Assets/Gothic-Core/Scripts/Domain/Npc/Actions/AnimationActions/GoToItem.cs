using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_GotoItem: walks to a world item (a wisp to what it found, a monster to food on the ground).
    /// </summary>
    public class GoToItem : AbstractWalkAnimationAction
    {
        // Close enough to pick it up.
        private const float _reachDistance = 0.6f;

        private readonly Transform _item;

        public GoToItem(AnimationAction action, NpcContainer npcContainer, Transform item) : base(action, npcContainer)
        {
            _item = item;
        }

        public override void Start()
        {
            if (_item == null)
            {
                IsFinishedFlag = true;
                return;
            }

            base.Start();
        }

        protected override Vector3 GetWalkDestination()
        {
            // Taken by somebody else meanwhile: stay.
            if (_item == null)
                return NpcGo.transform.position;

            var itemPos = _item.position;
            var toItem = itemPos - NpcGo.transform.position;
            toItem.y = 0f;
            if (toItem.sqrMagnitude < _reachDistance * _reachDistance)
                return NpcGo.transform.position;
            return itemPos - toItem.normalized * _reachDistance;
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
