using Gothic.Core.Models.Container;
using Gothic.Core.Extensions;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_LookAtNpc, and AI_Quicklook when Float0 holds its seconds. Neither waits for the head.
    /// </summary>
    public class LookAtNpc : AbstractAnimationAction
    {
        private Transform _otherHead => Action.Instance0.GetUserData().PrefabProps.Head.transform;


        public LookAtNpc(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        { }

        public override void Start()
        {
            if (Action.Float0 > 0f)
                PrefabProps.AnimationHeadHandler.StartQuickLook(_otherHead, Action.Float0);
            else
                PrefabProps.AnimationHeadHandler.StartLookAt(_otherHead);

            IsFinishedFlag = true;
        }
    }
}
