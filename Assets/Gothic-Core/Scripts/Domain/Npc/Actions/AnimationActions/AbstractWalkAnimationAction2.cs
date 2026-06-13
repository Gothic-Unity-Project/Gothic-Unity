using Gothic.Core.Const;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public abstract class AbstractWalkAnimationAction2 : AbstractAnimationAction
    {
        protected Transform NpcTransform => NpcGo.transform;
        protected bool IsDestReached;

        // Name of the animation StartWalk() actually played. StopWalk() must stop exactly this one:
        // recalculating the name would stop the wrong animation when walk/fight mode changed mid-walk
        // (e.g. via an immediately executed AI_SetWalkmode), leaving the walk loop sliding the NPC forever.
        private string _startedWalkAnimationName;

        protected AbstractWalkAnimationAction2(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        protected virtual void OnDestinationReached()
        {
            StopWalk();
        }

        /// <summary>
        /// We need to define the final destination spot within overriding class.
        /// </summary>
        protected abstract Vector3 GetWalkDestination();

        public override void Start()
        {
            base.Start();

            // NPCs spawn on top of a WP. We need to inform the implementing class to act (e.g. alter destination WP)
            if (IsDestinationReached())
            {
                OnDestinationReached();

                // Already at the final destination (e.g. a FP_ROAM FreePoint right next to the NPC):
                // never start the walk loop - nobody would stop it again and its root motion
                // would slide the NPC around (visible e.g. on roaming Molerats).
                // IsDestReached covers subclasses which continue at the spot without finishing
                // (e.g. UseMob playing its transition animation) - the walk loop would blend
                // that animation out again. Only a multi-stop route (GoToWp) resets the flag
                // and walks on.
                if (IsFinishedFlag || IsDestReached)
                    return;
            }

            StartWalk();
        }

        public override void Tick()
        {
            base.Tick();

            if (IsFinishedFlag)
            {
                return;
            }

            if (IsDestinationReached())
                OnDestinationReached();
            // Do not rotate when a destination is reached this frame. Either rotate next frame (e.g. GoToWP.nextRoute) or stop it fully.
            else
                HandleRotation();
        }

        protected virtual void StartWalk()
        {
            PhysicsService.EnablePhysicsForNpc(PrefabProps);

            _startedWalkAnimationName = AnimationService.GetAnimationName(VmGothicEnums.AnimationType.Move, NpcContainer);
            PrefabProps.AnimationSystem.PlayAnimation(_startedWalkAnimationName);
        }

        protected virtual void StopWalk()
        {
            PhysicsService.EnablePhysicsForNpc(PrefabProps);

            if (_startedWalkAnimationName != null)
            {
                PrefabProps.AnimationSystem.StopAnimation(_startedWalkAnimationName);
            }
        }

        private bool IsDestinationReached()
        {
            var npcPos = NpcTransform.position;
            var walkPos = GetWalkDestination();
            var npcDistPos = new Vector3(npcPos.x, walkPos.y, npcPos.z);

            var distance = Vector3.Distance(npcDistPos, walkPos);

            // FIXME - Scorpio is above FP, but values don't represent it.
            if (distance < Constants.NpcDestinationReachedThreshold)
            {
                IsDestReached = true;
            }

            return IsDestReached;
        }

        private void HandleRotation()
        {
            var destination = GetWalkDestination();
            var npcPos = NpcTransform.position;
            var sameHeightDirection = new Vector3(destination.x, npcPos.y, destination.z);
            var direction = (sameHeightDirection - npcPos);
            var destinationRotation = Quaternion.LookRotation(direction);
            NpcTransform.rotation = Quaternion.RotateTowards(NpcTransform.rotation, destinationRotation, Time.deltaTime * Constants.NpcRotationSpeed); 
        }
    }
}
