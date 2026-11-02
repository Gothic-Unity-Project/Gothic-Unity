using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_TurnAway: like AI_TurnToNpc, but the back faces the other NPC (ZS_TurnAway: a beaten guard looks away -
    /// "I didn't see anything").
    /// </summary>
    public class TurnAwayFromNpc : TurnToNpc
    {
        public TurnAwayFromNpc(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        protected override Quaternion GetRotationDirection()
        {
            var direction = NpcGo.transform.position - Action.Instance0.GetUserData().Go.transform.position;
            direction.y = 0;

            return direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, Vector3.up)
                : NpcGo.transform.rotation;
        }
    }
}
