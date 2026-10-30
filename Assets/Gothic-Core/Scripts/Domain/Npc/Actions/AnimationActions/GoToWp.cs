using System.Collections.Generic;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vob.WayNet;
using Gothic.Core.Models.WayNet;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class GoToWp : AbstractWalkAnimationAction2
    {
        // Within this distance (m) the NPC stands on the waypoint it reached last (OpenGothic: 40 cm, more for shared
        // points - our NPCs stop a bit less exactly).
        private const float _onWayPointDistance = 1f;

        private string Destination => Action.String0;

        private Stack<DijkstraWaypoint> _route;

        public GoToWp(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            // Like the engine (OpenGothic World::wayTo), the route starts at the waypoint the NPC stands on, else at the
            // nearest one. The stored one alone made an NPC interrupted on its way walk back to it first.
            var currentWaypoint = Props.CurrentWayPoint;
            if (currentWaypoint == null || (currentWaypoint.Position - NpcGo.transform.position).sqrMagnitude >
                _onWayPointDistance * _onWayPointDistance)
                currentWaypoint = WayNetService.FindNearestWayPoint(PrefabProps.Bip01.position);
            var destinationWaypoint = (WayPoint)WayNetService.GetWayNetPoint(Destination);
            
            /*
             * Two situations, when this action can be skipped:
             * 1. Ai_GoToWp() can get called multiple times until it will loose the WP in between the Ai_StartState() calls. (e.g. ZS_Sleep() -> ZS_StandAround())
             * 2. During spawning (e.g.). As we spawn NPCs onto their current WayPoints, they don't need to walk there from entrance of OC.
             */
            if (destinationWaypoint == null || destinationWaypoint.Name == "" || currentWaypoint.Name == Destination)
            {
                IsFinishedFlag = true;
                return;
            }

            // We need to set the route now to ensure base.Start() can check if NPC is already _on_ the final destination.
            var path = WayNetService.FindFastestPath(currentWaypoint.Name, destinationWaypoint.Name);
            if (path == null)
            {
                IsFinishedFlag = true;
                return;
            }

            _route = new Stack<DijkstraWaypoint>(path);

            base.Start();
        }

        protected override Vector3 GetWalkDestination()
        {
            return _route.Peek().Position;
        }

        protected override void AnimationEnd()
        {
            base.AnimationEnd();

            IsFinishedFlag = false;
        }

        protected override void OnDestinationReached()
        {
            // The waypoint the NPC is at now - where its next route starts (found on BoroBongo's branch).
            if (WayNetService.GetWayNetPoint(_route.Pop().Name) is WayPoint reached)
                Props.CurrentWayPoint = reached;

            if (_route.Count != 0)
            {
                // We need to reset this flag now. Otherwise, we will skip all movement elements...
                IsDestReached = false;
                return;
            }

            StopWalk();
            AnimationEnd();

            IsFinishedFlag = true;
        }
    }
}
