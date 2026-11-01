using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_WhirlAround like the engine (OpenGothic TurnType::Whirl): the startled jump around towards the other one
    /// (Humans.mds t_surprise_CW / t_surprise_CCW), the body turning with it. G1's B_WhirlAround uses it when the NPC
    /// can't see the hero, e.g. ZS_AssessFighter when he draws behind him. Models without the animation (monsters)
    /// just face the other one. Found on BoroBongo's branch.
    /// </summary>
    public class WhirlAround : AbstractAnimationAction
    {
        private const string _animationClockwise = "t_surprise_CW";
        private const string _animationCounterClockwise = "t_surprise_CCW";
        private const string _animationEndPose = "s_surprise";

        private Quaternion _startRotation;
        private Quaternion _finalRotation;
        private string _animationName;
        private float _duration;

        public WhirlAround(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            base.Start();

            var target = Action.Instance0?.GetUserData();
            var isUsingMob = PrefabProps.CurrentInteractable != null && Props.CurrentInteractableStateId >= 0;
            if (target?.Go == null || isUsingMob)
            {
                IsFinishedFlag = true;
                return;
            }

            var direction = target.Go.transform.position - NpcGo.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                IsFinishedFlag = true;
                return;
            }

            _startRotation = NpcGo.transform.rotation;
            _finalRotation = Quaternion.LookRotation(direction, Vector3.up);
            var isClockwise = Vector3.SignedAngle(NpcGo.transform.forward, direction, Vector3.up) > 0f;
            var animationName = isClockwise ? _animationClockwise : _animationCounterClockwise;

            if (!PrefabProps.AnimationSystem.PlayAnimation(animationName))
            {
                NpcGo.transform.rotation = _finalRotation;
                IsFinishedFlag = true;
                return;
            }

            _animationName = animationName;
            _duration = Mathf.Max(PrefabProps.AnimationSystem.GetAnimationDuration(animationName), 0.1f);
            ActionEndEventTime = _duration;
        }

        public override void Tick()
        {
            base.Tick();
            if (_animationName == null)
                return;

            var t = Mathf.Clamp01(ActionTime / _duration);
            NpcGo.transform.rotation = Quaternion.Slerp(_startRotation, _finalRotation, Mathf.SmoothStep(0f, 1f, t));
        }

        protected override void AnimationEnd()
        {
            NpcGo.transform.rotation = _finalRotation;
            // t_surprise_* goes on into s_surprise - nothing would end that pose if no other animation follows.
            PrefabProps.AnimationSystem.StopAnimation(_animationName);
            PrefabProps.AnimationSystem.StopAnimation(_animationEndPose);
            PrefabProps.AnimationSystem.PlayIdleAnimation();
            base.AnimationEnd();
        }

        public override void StopImmediately()
        {
            if (_animationName != null)
            {
                NpcGo.transform.rotation = _finalRotation;
                PrefabProps.AnimationSystem.StopAnimation(_animationName);
                PrefabProps.AnimationSystem.StopAnimation(_animationEndPose);
            }
            base.StopImmediately();
        }
    }
}
