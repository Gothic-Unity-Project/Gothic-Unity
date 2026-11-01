using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob.WayNet;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_Flee: like the engine (OpenGothic Npc::implAiFlee), the NPC runs away from its target along the waynet - to
    /// the waypoint within 5 m it can see that is farthest from the enemy. Without one farther from the enemy than the
    /// NPC itself, it runs straight away, sideways if a wall is right there.
    /// Each call is one short step - ZS_Flee's loop calls it again until the enemy is far enough. The run goes on from
    /// one step to the next and stops (back to standing) when no flee step follows, on Npc_ClearAIQueue and on a state
    /// change. Death and knockout take the body over themselves.
    /// </summary>
    public class Flee : AbstractWalkAnimationAction2
    {
        private const float _stepSeconds = 0.5f;
        private const float _wayPointSearchDistance = 5f;
        private const float _escapeDistance = 5f;
        private const float _obstacleCheckDistance = 2f;
        private const float _bodyHeight = 1f;
        private const float _wayPointSightHeight = 0.1f;
        private static readonly float[] _escapeAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f };
        // Walls as the NPC's line of sight sees them (NpcHelperService): the world and static vobs.
        private static readonly int _obstacleLayers = 1 << Constants.DefaultLayer;

        // ZS_Flee repeats AI_Flee every frame - a missing enemy is logged once only.
        private static bool _isMissingEnemyLogged;

        private Vector3 _destination;
        private float _stepDuration = _stepSeconds;
        private bool _isRunning;

        public Flee(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var enemy = GetEnemy();
            if (enemy == null)
            {
                if (!_isMissingEnemyLogged)
                {
                    _isMissingEnemyLogged = true;
                    Logger.LogWarning($"[Flee] {NpcInstance.GetName(NpcNameSlot.Slot0)} has no enemy (or no body) " +
                                      "to flee from - AI_Flee does nothing.", LogCat.Ai);
                }
                IsFinishedFlag = true;
                return;
            }

            // G1's ZS_Flee_Loop waits a second after each AI_Flee. Like in the engine, the NPC runs on meanwhile: the
            // step takes the wait over, so it keeps steering and still stops its run at the end.
            if (Props.AnimationQueue.TryPeek(out var next) && next is Wait)
            {
                Props.AnimationQueue.Dequeue();
                _stepDuration += next.Action.Float0;
            }

            _destination = GetFleeDestination(enemy);

            // The walk start: physics, BS_RUN in run mode and the move loop - or the previous step's loop goes on.
            _isRunning = true;
            base.Start();
        }

        public override void Tick()
        {
            // Death and knockout take the body over: they replace the run and own the body state.
            if (IsNpcDown())
            {
                _isRunning = false;
                IsFinishedFlag = true;
                return;
            }

            // The step ended with an empty queue. ZS_Flee's loop has run since and queued the next step - or not.
            if (IsFinishedFlag)
            {
                HandOverOrStopRun();
                return;
            }

            base.Tick();

            if (!IsFinishedFlag && ActionTime >= _stepDuration)
                EndStep();
        }

        public override void StopImmediately()
        {
            StopRun();
            base.StopImmediately();
        }

        protected override Vector3 GetWalkDestination()
        {
            return _destination;
        }

        protected override void OnDestinationReached()
        {
            EndStep();
        }

        private void EndStep()
        {
            IsFinishedFlag = true;

            // AiHandler starts a queued action right away, so decide now. With an empty queue the state loop runs
            // first, Tick() decides next frame.
            if (Props.AnimationQueue.Count > 0)
                HandOverOrStopRun();
        }

        /// <summary>
        /// The next flee step goes on with the run, without a stop and restart of the loop. Anything else ends the
        /// flight.
        /// </summary>
        private void HandOverOrStopRun()
        {
            if (!_isRunning)
                return;

            // Same frame as the next step's Start(): it runs if it finds the enemy, too.
            if (Props.AnimationQueue.TryPeek(out var next) && next is Flee && GetEnemy() != null)
            {
                _isRunning = false;
                return;
            }

            StopRun();
        }

        private void StopRun()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            if (!IsNpcDown())
                StopWalk();
        }

        private NpcContainer GetEnemy()
        {
            var enemy = (Props.TargetNpc ?? Props.EnemyNpc)?.GetUserData();
            return enemy?.Go == null || NpcGo == null ? null : enemy;
        }

        private bool IsNpcDown()
        {
            return Props.IsDead || Props.BodyState == VmGothicEnums.BodyState.BsUnconscious;
        }

        /// <summary>
        /// Like the engine: the waypoint within 5 m the NPC can see that is farthest from the enemy - if it is farther
        /// from the enemy than the NPC. Otherwise a point straight away from the enemy.
        /// </summary>
        private Vector3 GetFleeDestination(NpcContainer enemy)
        {
            const float searchDistanceSqr = _wayPointSearchDistance * _wayPointSearchDistance;
            const float reachedDistanceSqr =
                Constants.NpcDestinationReachedThreshold * Constants.NpcDestinationReachedThreshold;

            var position = NpcTransform.position;
            var enemyPosition = enemy.Go.transform.position;
            var eyes = position + Vector3.up * _bodyHeight;

            WayPoint bestWayPoint = null;
            var bestEnemyDistanceSqr = (position - enemyPosition).sqrMagnitude;
            foreach (var wayPoint in GameStateService.WayPoints.Values)
            {
                var toWayPoint = wayPoint.Position - position;
                if (wayPoint.IsLocked || toWayPoint.sqrMagnitude > searchDistanceSqr)
                    continue;

                // The waypoint the NPC stands on leads nowhere (and would count as reached right away).
                toWayPoint.y = 0f;
                if (toWayPoint.sqrMagnitude < reachedDistanceSqr)
                    continue;

                var enemyDistanceSqr = (wayPoint.Position - enemyPosition).sqrMagnitude;
                if (enemyDistanceSqr <= bestEnemyDistanceSqr)
                    continue;

                if (Physics.Linecast(eyes, wayPoint.Position + Vector3.up * _wayPointSightHeight, _obstacleLayers,
                        QueryTriggerInteraction.Ignore))
                    continue;

                bestWayPoint = wayPoint;
                bestEnemyDistanceSqr = enemyDistanceSqr;
            }

            if (bestWayPoint != null)
                return bestWayPoint.Position;

            return position + GetEscapeDirection(position, enemyPosition) * _escapeDistance;
        }

        /// <summary>
        /// Away from the enemy; if a wall is right there, the free direction closest to that.
        /// </summary>
        private Vector3 GetEscapeDirection(Vector3 position, Vector3 enemyPosition)
        {
            var away = position - enemyPosition;
            if (new Vector2(away.x, away.z).sqrMagnitude < 0.0001f)
                away = -NpcTransform.forward;
            away.y = 0f;
            away.Normalize();

            var origin = position + Vector3.up * _bodyHeight;
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
