using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_TurnAway: the NPC turns its back to the other one - beaten NPCs look away ("I didn't see anything").
    /// </summary>
    public class TurnAwayFromNpc : AbstractRotateAnimationAction
    {
        public TurnAwayFromNpc(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
            PlayAnimation = true;
        }

        protected override Quaternion GetRotationDirection()
        {
            var other = Action.Instance0.GetUserData();
            if (other?.Go == null)
                return NpcGo.transform.rotation;

            var away = NpcGo.transform.position - other.Go.transform.position;
            away.y = 0f;
            return away.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(away, Vector3.up) : NpcGo.transform.rotation;
        }
    }
}
