using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class AlignToFp : AbstractRotateAnimationAction
    {
        public AlignToFp(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        protected override Quaternion GetRotationDirection()
        {
            // No free point (none was free for AI_GotoFP): the NPC keeps its direction, like the engine.
            var freePoint = Props.CurrentFreePoint;
            if (freePoint == null)
            {
                // Routines align on every loop pass - warned once per NPC and world.
                Logger.LogWarningOnce($"AlignToFp.{NpcInstance.Index}",
                    $"AI_AlignToFP: {NpcInstance.GetName(NpcNameSlot.Slot0)} has no free point - " +
                    "keeps its direction.", LogCat.Ai);
                return NpcGo.transform.rotation;
            }

            return Quaternion.Euler(freePoint.Direction);
        }
    }
}
