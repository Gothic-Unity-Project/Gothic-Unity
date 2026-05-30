using GUZ.Core.Extensions;
using GUZ.Core.Models.Container;
using GUZ.Core.Models.Vm;
using UnityEngine;
using ZenKit.Daedalus;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// Basically PlayAni with special Attack handling.
    /// </summary>
    public class AttackPlayAni : PlayAni
    {
        private FightAiMove _move => (FightAiMove)Action.Int0;
        private NpcContainer _enemy => Action.Instance0.GetUserData();
        private Transform _enemyTransform => _enemy.Go.transform;


        public AttackPlayAni(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Tick()
        {
            base.Tick();

            switch (_move)
            {
                case FightAiMove.Run:
                    RunTick();
                    break;
                case FightAiMove.Strafe:
                    StrafeTick();
                    break;
            }
        }

        private void RunTick()
        {
            var myPosition = NpcContainer.Go.transform.position;
            var targetPosition = Action.Instance0.GetUserData()!.Go.transform.position;

            // Consider only horizontal distance (ignore Y-axis)
            var myPositionHorizontal = new Vector3(myPosition.x, 0, myPosition.z);
            var targetPositionHorizontal = new Vector3(targetPosition.x, 0, targetPosition.z);
            var distance = Vector3.Distance(myPositionHorizontal, targetPositionHorizontal);

            if (distance <= 1f)
            {
                PrefabProps.AnimationSystem.StopAllAnimations();
                IsFinishedFlag = true;
            }
        }

        private void StrafeTick()
        {
            // For rotation speed, we use the guild value for human if any type of human or the monster guild itself.
            var guild = NpcInstance.Guild <= (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM ? (int)VmGothicEnums.Guild.GIL_HUMAN : NpcInstance.Guild;

            // e.g., Goblins aren't rotating fast enough to rotate around the target. Therefore *2;
            var turnSpeed = GameStateService.GuildValues.GetTurnSpeed(guild) * 2;
            var currentRotation =
                Quaternion.RotateTowards(NpcGo.transform.rotation, GetRotationDirection(), Time.deltaTime * turnSpeed);

            NpcGo.transform.rotation = currentRotation;
        }

        private Quaternion GetRotationDirection()
        {
            var destinationTransform = _enemyTransform;
            // var temp = destinationTransform.position - NpcGo.transform.position;
            // return Quaternion.LookRotation(temp, Vector3.up);
            // }
            var direction = destinationTransform.position - NpcGo.transform.position;

            // Ensure the direction only affects horizontal rotation (Y-axis)
            direction.y = 0;

            // Check if the direction vector is not zero, to prevent zero-length rotation issues
            if (direction.sqrMagnitude > 0.0001f)
            {
                // Return the rotation required to face the player, constrained to the Y-axis
                return Quaternion.LookRotation(direction, Vector3.up);
            }
            else
            {
                // If the player is directly at the same position, maintain the current rotation
                return NpcGo.transform.rotation;
            }
        }
    }
}
