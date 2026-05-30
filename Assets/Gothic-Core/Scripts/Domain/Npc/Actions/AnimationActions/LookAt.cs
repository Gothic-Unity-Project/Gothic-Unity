using GUZ.Core.Models.Container;
using UnityEngine;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class LookAt : AbstractRotateAnimationAction
    {
        private Transform _destinationTransform;
        private string WaypointName => Action.String0;

        public LookAt(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        protected override Quaternion GetRotationDirection()
        {
            var euler = WayNetService.GetWayNetPoint(WaypointName).Direction;
            return Quaternion.Euler(euler);
        }
    }
}
