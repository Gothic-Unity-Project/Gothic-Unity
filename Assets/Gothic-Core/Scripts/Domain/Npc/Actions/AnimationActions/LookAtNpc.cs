using GUZ.Core.Models.Container;
using GUZ.Core.Extensions;
using UnityEngine;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class LookAtNpc : AbstractAnimationAction
    {
        private Transform _otherHead => Action.Instance0.GetUserData().PrefabProps.Head.transform;


        public LookAtNpc(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        { }

        public override void Start()
        {
            PrefabProps.AnimationHeadHandler.StartLookAt(_otherHead);

            IsFinishedFlag = true;
        }
    }
}
