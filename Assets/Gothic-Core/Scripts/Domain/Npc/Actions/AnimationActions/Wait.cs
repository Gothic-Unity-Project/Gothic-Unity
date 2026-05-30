using GUZ.Core.Models.Container;
using UnityEngine;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class Wait : AbstractAnimationAction
    {
        private float _waitSeconds;

        public Wait(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            _waitSeconds = Action.Float0;
        }

        public override bool IsFinished()
        {
            _waitSeconds -= Time.deltaTime;

            return _waitSeconds <= 0f;
        }
    }
}
