using Gothic.Core.Models.Container;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class GoToNpc : AbstractWalkAnimationAction2
    {
        [Inject] private readonly ConfigService _configService;

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
            return targetPos + toTarget.normalized * -_configService.Dev.NpcDialogStopDistance;
        }

        protected override void OnDestinationReached()
        {
            base.OnDestinationReached();
            IsFinishedFlag = true;
        }
    }
}
