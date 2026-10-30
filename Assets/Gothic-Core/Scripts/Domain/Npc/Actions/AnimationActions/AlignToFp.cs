using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class AlignToFp : AbstractRotateAnimationAction
    {
        // Routines align on every loop pass - warned once per NPC.
        private static readonly HashSet<int> _npcsWarnedWithoutFp = new();

        public AlignToFp(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        protected override Quaternion GetRotationDirection()
        {
            // No free point (none was free for AI_GotoFP): the NPC keeps its direction, like the engine.
            var freePoint = Props.CurrentFreePoint;
            if (freePoint == null)
            {
                if (_npcsWarnedWithoutFp.Add(NpcInstance.Index))
                    Logger.LogWarning($"AI_AlignToFP: {NpcInstance.GetName(NpcNameSlot.Slot0)} has no free point - " +
                                      "keeps its direction.", LogCat.Ai);
                return NpcGo.transform.rotation;
            }

            return Quaternion.Euler(freePoint.Direction);
        }
    }
}
