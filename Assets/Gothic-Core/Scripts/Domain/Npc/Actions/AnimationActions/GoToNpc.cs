using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vob.WayNet;
using Gothic.Core.Models.WayNet;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class GoToNpc : AbstractWalkAnimationAction2
    {
        [Inject] private readonly ConfigService _configService;

        private Transform _destinationTransform;
        private Stack<DijkstraWaypoint> _route;

        public GoToNpc(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            _destinationTransform = Action.Instance0.GetUserData().Go.transform;

            var currentWp = Props.CurrentWayPoint ?? WayNetService.FindNearestWayPoint(PrefabProps.Bip01.position);
            var targetWp = WayNetService.FindNearestWayPoint(_destinationTransform.position);

            if (currentWp != null && targetWp != null && currentWp.Name != targetWp.Name)
            {
                var path = WayNetService.FindFastestPath(currentWp.Name, targetWp.Name);
                if (path != null && path.Length > 0)
                {
                    _route = new Stack<DijkstraWaypoint>(path);
                    Logger.Log($"[GoToNpc] {NpcInstance.GetName(NpcNameSlot.Slot0)}: WP route {currentWp.Name}→{targetWp.Name} steps={path.Length}", LogCat.Ai);
                }
            }

            base.Start();
        }

        protected override Vector3 GetWalkDestination()
        {
            if (_route != null && _route.Count > 0)
                return _route.Peek().Position;

            var targetPos = _destinationTransform.position;
            var toTarget = targetPos - NpcGo.transform.position;
            if (toTarget.sqrMagnitude < 0.001f)
                return targetPos;
            return targetPos + toTarget.normalized * -_configService.Dev.NpcDialogStopDistance;
        }

        protected override void OnDestinationReached()
        {
            if (_route != null && _route.Count > 0)
            {
                var reached = _route.Pop();
                var reachedWp = WayNetService.GetWayNetPoint(reached.Name) as WayPoint;
                if (reachedWp != null)
                    Props.CurrentWayPoint = reachedWp;

                if (_route.Count > 0)
                {
                    IsDestReached = false;
                    return;
                }

                _route = null;
                IsDestReached = false;
                return;
            }

            base.OnDestinationReached();
            IsFinishedFlag = true;
        }
    }
}
