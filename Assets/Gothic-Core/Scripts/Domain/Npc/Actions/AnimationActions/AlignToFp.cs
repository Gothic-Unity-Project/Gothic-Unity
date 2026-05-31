using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class AlignToFp : AbstractRotateAnimationAction
    {
        public AlignToFp(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        protected override Quaternion GetRotationDirection()
        {
            var euler = Props.CurrentFreePoint.Direction;

            return Quaternion.Euler(euler);
        }
    }
}
