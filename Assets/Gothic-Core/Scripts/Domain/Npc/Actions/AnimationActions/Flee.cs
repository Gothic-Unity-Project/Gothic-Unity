using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using UnityEngine;
using UnityEngine.AI;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_Flee (DeveloperConfig.EnableAiFlee): runs a few meters away from the enemy (Action.Instance0) - ZS_Flee_Loop
    /// issues it again every loop, so the NPC keeps running while the fear lasts. Without it, scared NPCs stood still.
    /// </summary>
    public class Flee : AbstractWalkAnimationAction2
    {
        private const float _fleeDistance = 6f;
        private const float _maxSeconds = 3f;
        private const float _navMeshSampleDistance = 3f;

        private Vector3 _destination;
        private float _startTime;

        public Flee(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            _startTime = Time.time;
            var position = NpcGo.transform.position;
            var enemyGo = Action.Instance0?.GetUserData()?.Go;
            var away = enemyGo != null ? position - enemyGo.transform.position : -NpcGo.transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = -NpcGo.transform.forward;

            _destination = position + away.normalized * _fleeDistance;
            if (NavMesh.SamplePosition(_destination, out var hit, _navMeshSampleDistance, NavMesh.AllAreas))
                _destination = hit.position;

            base.Start();
        }

        protected override Vector3 GetWalkDestination()
        {
            return _destination;
        }

        public override void Tick()
        {
            if (!IsFinishedFlag && Time.time - _startTime > _maxSeconds)
            {
                StopWalk();
                IsFinishedFlag = true;
                return;
            }
            base.Tick();
        }

        protected override void OnDestinationReached()
        {
            base.OnDestinationReached();
            IsFinishedFlag = true;
        }
    }
}
