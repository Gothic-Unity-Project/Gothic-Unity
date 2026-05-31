using System;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class AlignToWp : AbstractRotateAnimationAction
    {
        public AlignToWp(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        protected override Quaternion GetRotationDirection()
        {
            try
            {
                var currentWaypoint = Props.CurrentWayPoint ?? WayNetService.FindNearestWayPoint(PrefabProps.Bip01.position);

                return Quaternion.Euler(currentWaypoint.Direction);
            }
            catch (Exception e)
            {
                Logger.LogError(e.ToString(), LogCat.Ai);
                return Quaternion.identity;
            }
        }
    }
}
