using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// DeveloperConfig.EnableAiWhirlAround: AI_WhirlAround like the engine - the startled jump around towards the
    /// target (Humans.mds t_surprise_CW / t_surprise_CCW, 16 frames), the body turns with it. G1 B_WhirlAround uses it
    /// when the NPC can't see the target, e.g. ZS_AssessFighter when the hero draws a weapon or a rune behind him.
    /// Models without the animation (monsters) turn normally.
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
            _animationName = isClockwise ? _animationClockwise : _animationCounterClockwise;

            if (!PrefabProps.AnimationSystem.PlayAnimation(_animationName))
            {
                // No startled jump (monster model): just face the target.
                NpcGo.transform.rotation = _finalRotation;
                IsFinishedFlag = true;
                return;
            }

            _duration = Mathf.Max(PrefabProps.AnimationSystem.GetAnimationDuration(_animationName), 0.1f);
            ActionEndEventTime = _duration;
            Logger.Log($"[WhirlAround] {NpcInstance.GetName(NpcNameSlot.Slot0)} whirls around to " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)} ({_animationName})", LogCat.Ai);
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
            // t_surprise_* continues with s_surprise - nothing ends that pose if no other animation follows.
            PrefabProps.AnimationSystem.StopAnimation(_animationName);
            PrefabProps.AnimationSystem.StopAnimation(_animationEndPose);
            PrefabProps.AnimationSystem.PlayIdleAnimation();
            base.AnimationEnd();
        }
    }
}
