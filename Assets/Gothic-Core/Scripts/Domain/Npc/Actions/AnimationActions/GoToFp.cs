using GUZ.Core.Models.Container;
using GUZ.Core.Models.Vob.WayNet;
using UnityEngine;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class GoToFp : AbstractWalkAnimationAction2
    {
        private FreePoint _fp;

        private string _destination => Action.String0;

        private FreePoint _freePoint;

        public GoToFp(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var npcPos = NpcGo.transform.position;
            _fp = WayNetService.FindNearestFreePoint(npcPos, _destination, Props.CurrentFreePoint);

            if (_fp == null)
            {
                IsFinishedFlag = true;
                return;
            }

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
