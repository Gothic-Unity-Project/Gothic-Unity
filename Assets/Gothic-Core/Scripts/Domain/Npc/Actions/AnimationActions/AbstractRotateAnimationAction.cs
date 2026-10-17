using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Npc;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public abstract class AbstractRotateAnimationAction : AbstractAnimationAction
    {
        // Can be used to rotate without animation.
        protected bool PlayAnimation = true;

        private Quaternion _finalRotation;
        private bool _isRotateLeft;
        private string _rotationAnimationName;

        private Transform NpcHeadTransform => PrefabProps.Head;

        protected AbstractRotateAnimationAction(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        /// <summary>
        /// We need to define the final direction within overriding class.
        /// </summary>
        protected abstract Quaternion GetRotationDirection();

        private Quaternion GetDesiredHeadRotation()
        {
            // Get the current forward direction of the NPC's body
            var currentBodyForwardDirection = NpcGo.transform.TransformDirection(Vector3.forward);

            // Calculate the desired rotation for the head to look in the current body's forward direction
            var desiredHeadRotation = Quaternion.LookRotation(currentBodyForwardDirection);

            // Adjust the desired head rotation to prevent the head from resting on the shoulder
            desiredHeadRotation *= Quaternion.Euler(0f, -30f, 90f); // Reset pitch and roll

            return desiredHeadRotation;
        }


        public override void Start()
        {
            // Like the engine (OpenGothic Npc::isRotationAllowed): an NPC using a mob (sitting on a bench, ...), lying or
            // climbing doesn't turn. Sitting poses are turned around inside the animation (AnimationSystem
            // _isSittingInverted), so turning the root towards the hero showed him the NPC's back.
            if (ConfigService.Dev.EnableNoTurnWhileUsingMob && !IsRotationAllowed())
            {
                IsFinishedFlag = true;
                return;
            }

            _finalRotation = GetRotationDirection();

            // Already aligned.
            if (Quaternion.Angle(NpcGo.transform.rotation, _finalRotation) < 1f)
            {
                IsFinishedFlag = true;
                return;
            }

            // Negative signed angle around the up axis means the target direction is to our left.
            var targetForward = _finalRotation * Vector3.forward;
            _isRotateLeft = Vector3.SignedAngle(NpcGo.transform.forward, targetForward, Vector3.up) < 0;

            if (PlayAnimation)
            {
                _rotationAnimationName = AnimationService.GetAnimationName(
                    _isRotateLeft ? VmGothicEnums.AnimationType.RotL : VmGothicEnums.AnimationType.RotR,
                    NpcContainer);
                PrefabProps.AnimationSystem.PlayAnimation(_rotationAnimationName);
            }
        }

        private bool IsRotationAllowed()
        {
            // BodyState and CurrentInteractable are set while still walking to the mob already. State -1 = not in use.
            var isUsingMob = PrefabProps.CurrentInteractable != null && Props.CurrentInteractableStateId >= 0;
            return !isUsingMob;
        }

        public override void Tick()
        {
            base.Tick();

            if (IsFinishedFlag)
                return;

            // Re-fetch every tick instead of reusing the Start() snapshot — the destination can be a
            // moving NPC (TurnToNpc tracking a VR player who keeps repositioning). A stale one-time
            // snapshot leaves the turn permanently aimed at where the target *was*, so it finishes
            // "aligned" to an outdated direction and immediately falls out of focus again next round.
            _finalRotation = GetRotationDirection();
            HandleRotation(NpcGo.transform);
        }

        /// <summary>
        /// Unfortunately it seems that G1 rotation animations have no root motions for the rotation (unlike walking).
        /// We therefore need to set it manually here.
        /// </summary>
        private void HandleRotation(Transform npcTransform)
        {
            // For rotation speed, we use the guild value for human if any type of human or the monster guild itself.
            var guild = NpcInstance.Guild <= (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM ? (int)VmGothicEnums.Guild.GIL_HUMAN : NpcInstance.Guild;

            var turnSpeed = GameStateService.GuildValues.GetTurnSpeed(guild);
            var currentRotation =
                Quaternion.RotateTowards(npcTransform.rotation, _finalRotation, Time.deltaTime * turnSpeed);

            // Check if rotation is done.
            if (Quaternion.Angle(npcTransform.rotation, _finalRotation) < 1f)
            {
                if (_rotationAnimationName != null)
                    PrefabProps.AnimationSystem.StopAnimation(_rotationAnimationName);

                IsFinishedFlag = true;
            }
            else
            {
                npcTransform.rotation = currentRotation;

                // Many monsters (e.g. Bloodflies) have no head.
                if (NpcHeadTransform)
                    NpcHeadTransform.rotation = GetDesiredHeadRotation();
            }
        }

        protected override void AnimationEnd()
        {
            base.AnimationEnd();
            IsFinishedFlag = false;
        }
    }
}
