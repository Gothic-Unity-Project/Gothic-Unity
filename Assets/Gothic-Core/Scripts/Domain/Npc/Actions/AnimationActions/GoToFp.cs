using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vob.WayNet;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class GoToFp : AbstractWalkAnimationAction2
    {
        private FreePoint _fp;

        private string _destination => Action.String0;

        public GoToFp(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var npcPos = NpcGo.transform.position;
            _fp = WayNetService.FindNearestFreePoint(npcPos, _destination, Props.CurrentFreePoint);

            if (_fp == null)
            {
                Logger.Log($"[GoToFp] {NpcInstance.GetName(NpcNameSlot.Slot0)}: pattern={_destination} no FP found npcPos=({npcPos.x:F1},{npcPos.y:F1},{npcPos.z:F1})", LogCat.Ai);
                IsFinishedFlag = true;
                return;
            }
            Logger.Log($"[GoToFp] {NpcInstance.GetName(NpcNameSlot.Slot0)}: pattern={_destination} fp={_fp.Name} fpPos=({_fp.Position.x:F1},{_fp.Position.y:F1},{_fp.Position.z:F1}) npcPos=({npcPos.x:F1},{npcPos.y:F1},{npcPos.z:F1})", LogCat.Ai);

            // Free the FP we still hold from a previous GoToFp. Otherwise every FP an NPC ever
            // visited stays locked until the NPC is culled, and roaming runs out of free FPs.
            if (Props.CurrentFreePoint != null && Props.CurrentFreePoint != _fp)
                Props.CurrentFreePoint.IsLocked = false;

            _fp.IsLocked = true;
            Props.CurrentFreePoint = _fp;

            base.Start();
        }

        protected override Vector3 GetWalkDestination()
        {
            return _fp.Position;
        }

        protected override void OnDestinationReached()
        {
            base.OnDestinationReached();
            
            IsFinishedFlag = true;
        }
    }
}
