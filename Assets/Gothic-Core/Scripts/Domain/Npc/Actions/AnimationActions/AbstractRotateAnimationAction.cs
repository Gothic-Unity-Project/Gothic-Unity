using GUZ.Core.Models.Container;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services;
using GUZ.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
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
            _finalRotation = GetRotationDirection();

            // Already aligned.
            if (Quaternion.Angle(NpcGo.transform.rotation, _finalRotation) < 1f)
            {
                IsFinishedFlag = true;
                return;
            }

            // https://discussions.unity.com/t/determining-whether-to-rotate-left-or-right/44021
            var cross = Vector3.Cross(NpcGo.transform.forward, _finalRotation.eulerAngles);
            _isRotateLeft = cross.y >= 0;

            if (PlayAnimation)
            {
                _rotationAnimationName = AnimationService.GetAnimationName(
                    _isRotateLeft ? VmGothicEnums.AnimationType.RotL : VmGothicEnums.AnimationType.RotR,
                    NpcContainer);
                PrefabProps.AnimationSystem.PlayAnimation(_rotationAnimationName);
            }
        }

        public override void Tick()
        {
            base.Tick();

            if (IsFinishedFlag)
                return;

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
