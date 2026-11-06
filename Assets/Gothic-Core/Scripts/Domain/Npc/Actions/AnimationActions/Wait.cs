using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
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

        /// <summary>
        /// A fight's wait ends when the hero swings - the next AI_Attack reacts.
        /// </summary>
        public void Interrupt()
        {
            _waitSeconds = 0f;
        }

        public override bool IsFinished()
        {
            _waitSeconds -= Time.deltaTime;

            return _waitSeconds <= 0f;
        }

        public override bool HasEnded => _waitSeconds <= 0f;

        /// <summary>
        /// Npc_ClearAIQueue ends an AI_Wait like the engine.
        /// </summary>
        public override void StopImmediately()
        {
            _waitSeconds = 0f;
            base.StopImmediately();
        }
    }
}
