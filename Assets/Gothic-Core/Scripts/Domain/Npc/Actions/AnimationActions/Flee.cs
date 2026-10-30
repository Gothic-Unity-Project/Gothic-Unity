using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_Flee: like the engine (OpenGothic Npc::implAiFlee), the NPC turns away from its enemy and runs. Each call is
    /// one short step - ZS_Flee's loop calls it again until the enemy is far enough. A wall ahead turns the escape
    /// sideways. The run animation keeps playing into the next step.
    /// </summary>
    public class Flee : AbstractAnimationAction
    {
        private const float _stepSeconds = 0.5f;
        private const float _turnSpeedDegrees = 360f;
        private const float _obstacleCheckDistance = 2f;
        private const float _bodyHeight = 1f;
        private static readonly float[] _escapeAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f };
        private static readonly int _obstacleLayers = 1 << Constants.DefaultLayer;

        private Vector3 _direction;

        public Flee(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            base.Start();

            var enemy = (Props.TargetNpc ?? Props.EnemyNpc)?.GetUserData();
            if (enemy?.Go == null || NpcGo == null)
            {
                IsFinishedFlag = true;
                return;
            }

            _direction = GetEscapeDirection(enemy);
            var runAnimation = AnimationService.GetAnimationName(VmGothicEnums.AnimationType.Move, NpcContainer);
            if (!PrefabProps.AnimationSystem.IsPlaying(runAnimation))
                PrefabProps.AnimationSystem.PlayAnimation(runAnimation);

            // Like walking, fleeing NPCs are moved by physics (falling, slopes).
            PhysicsService.EnablePhysicsForNpc(PrefabProps);
        }

        public override void Tick()
        {
            base.Tick();
            if (IsFinishedFlag)
                return;

            if (_direction.sqrMagnitude > 0.0001f)
                NpcGo.transform.rotation = Quaternion.RotateTowards(NpcGo.transform.rotation,
                    Quaternion.LookRotation(_direction), _turnSpeedDegrees * Time.deltaTime);

            if (ActionTime >= _stepSeconds)
                IsFinishedFlag = true;
        }

        /// <summary>
        /// Away from the enemy; if a wall is right there, the free direction closest to that.
        /// </summary>
        private Vector3 GetEscapeDirection(NpcContainer enemy)
        {
            var away = NpcGo.transform.position - enemy.Go.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = -NpcGo.transform.forward;
            away.Normalize();

            var origin = NpcGo.transform.position + Vector3.up * _bodyHeight;
            foreach (var angle in _escapeAngles)
            {
                var direction = Quaternion.Euler(0f, angle, 0f) * away;
                if (!Physics.Raycast(origin, direction, _obstacleCheckDistance, _obstacleLayers,
                        QueryTriggerInteraction.Ignore))
                    return direction;
            }

            return away;
        }
    }
}
