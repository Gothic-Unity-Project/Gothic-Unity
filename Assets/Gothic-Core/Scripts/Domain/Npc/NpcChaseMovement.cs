using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// How a fighting NPC runs after its target (AttackPlayAni's FightAiMove.Run): steering along the NavMesh, climbing
    /// a ledge or ladder, jumping down a survivable drop, stopping in front of an unreachable target - and the give-up
    /// timer when the hero keeps running away. One per attack action, created with <c>new(...).Inject()</c>.
    /// The walk actions (AbstractWalkAnimationAction2) have their own ladder handling for waypoint routes.
    /// </summary>
    public class NpcChaseMovement
    {
        public enum Step
        {
            // Keep running, the NPC turned towards its target (or the next NavMesh corner).
            Running,
            // NpcJumpFall took over (climb/ladder) - the attack action ends.
            JumpFallStarted,
            // The target can't be reached and the NPC stands at the closest point - stop running.
            Unreachable
        }

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcNavMeshService _npcNavMeshService;
        [Inject] private readonly INpcJumpFallFactory _npcJumpFallFactory;

        // NavMesh steering while running (DeveloperConfig.EnableNpcNavMesh), recalculated a few times per second.
        private const float _pathUpdateInterval = 0.2f;
        private const float _unreachableStopDistance = 0.6f;
        private const float _targetAboveHeight = 0.5f;
        private const float _targetBelowHeight = 0.8f;

        private const float _chaseGiveUpDuration = 10f;
        private const float _heroStopResetDelay = 2f;
        private const float _heroRunSpeedThreshold = 2f;

        private readonly NpcContainer _npc;

        private float _nextPathUpdateTime;
        private bool _hasSteerPoint;
        private Vector3 _steerPoint;
        private bool _isTargetReachable = true;

        private float _chaseTimer;
        private float _heroStopTimer;
        private Vector3 _previousHeroPos;
        private bool _isFirstGiveUpTick = true;

        /// <summary>
        /// Seconds the hero has been running away (see HasHeroRunAway).
        /// </summary>
        public float ChaseSeconds => _chaseTimer;


        public NpcChaseMovement(NpcContainer npc)
        {
            _npc = npc;
        }

        /// <summary>
        /// One frame of the run: turns towards the target - or along the NavMesh path around obstacles. An unreachable
        /// target (on a rock, down a cliff): climb the ledge in front, take the ladder on the path, jump down a
        /// survivable drop (engine), else stop at the closest point.
        /// </summary>
        /// <param name="toTarget">Horizontal direction to the target.</param>
        public Step Steer(Vector3 myPosition, Vector3 targetPosition, Vector3 toTarget)
        {
            // Rotate toward actual enemy while running (not toward the offset approach point).
            var runDirection = toTarget;
            var hasSteerDirection = TryGetSteerDirection(myPosition, targetPosition, out var steerDirection);

            if (TryStartClimbOrLadder(myPosition, targetPosition, toTarget, hasSteerDirection))
                return Step.JumpFallStarted;

            // The enemy is below and can't be reached on the NavMesh: jump down if the drop is survivable (engine).
            var isDroppingDown = hasSteerDirection && !_isTargetReachable && IsTargetBelow(myPosition, targetPosition) &&
                                 _npcJumpFallFactory.Get(_npc)?.IsSafeDropAhead(toTarget) == true;

            if (hasSteerDirection && !isDroppingDown)
            {
                if (!_isTargetReachable && steerDirection.magnitude < _unreachableStopDistance)
                    return Step.Unreachable;
                runDirection = steerDirection;
            }

            TurnTowards(runDirection);
            return Step.Running;
        }

        /// <summary>
        /// Give-up: tracks the hero's speed via his position delta. While he runs, the chase timer counts up; it only
        /// resets after he stood still for a while. True once he ran away for too long.
        /// </summary>
        public bool HasHeroRunAway()
        {
            var heroGo = ((NpcInstance)_gameStateService.GothicVm.GlobalHero).GetUserData().Go;
            var heroPos = heroGo.transform.position;

            if (_isFirstGiveUpTick)
            {
                _previousHeroPos = heroPos;
                _isFirstGiveUpTick = false;
            }

            var heroSpeed = Vector3.Distance(heroPos, _previousHeroPos) / Time.deltaTime;
            _previousHeroPos = heroPos;

            if (heroSpeed > _heroRunSpeedThreshold)
            {
                _chaseTimer += Time.deltaTime;
                _heroStopTimer = 0f;
            }
            else
            {
                _heroStopTimer += Time.deltaTime;
                if (_heroStopTimer >= _heroStopResetDelay)
                {
                    _chaseTimer = 0f;
                    _heroStopTimer = 0f;
                }
            }

            return _chaseTimer >= _chaseGiveUpDuration;
        }

        /// <summary>
        /// The enemy stands higher (and the NavMesh has no way up): climb the ledge in front like the engine does.
        /// The path goes over a ladder (NavMesh link): climb it.
        /// </summary>
        private bool TryStartClimbOrLadder(Vector3 myPosition, Vector3 targetPosition, Vector3 toTarget,
            bool hasSteerDirection)
        {
            if ((!hasSteerDirection || !_isTargetReachable) && IsTargetAbove(myPosition, targetPosition) &&
                _npcJumpFallFactory.Get(_npc)?.TryStartClimb(toTarget) == true)
                return true;

            return hasSteerDirection &&
                   _npcNavMeshService.TryGetLadder(myPosition, _steerPoint, out var ladderStart, out var ladderEnd) &&
                   _npcJumpFallFactory.Get(_npc)?.TryStartLadder(ladderStart, ladderEnd) == true;
        }

        private void TurnTowards(Vector3 direction)
        {
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            var turnSpeed = _gameStateService.GuildValues.GetTurnSpeed(_npc.Instance.GetValuesGuild());
            var npcTransform = _npc.Go.transform;
            npcTransform.rotation = Quaternion.RotateTowards(npcTransform.rotation, Quaternion.LookRotation(direction),
                Time.deltaTime * turnSpeed);
        }

        /// <summary>
        /// Horizontal direction to the next NavMesh corner. False without a usable NavMesh - run straight then.
        /// </summary>
        private bool TryGetSteerDirection(Vector3 myPosition, Vector3 targetPosition, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (Time.time >= _nextPathUpdateTime)
            {
                _nextPathUpdateTime = Time.time + _pathUpdateInterval;
                _hasSteerPoint = _npcNavMeshService.TryGetSteerPoint(_npc, targetPosition, out _steerPoint,
                    out _isTargetReachable);
            }

            if (!_hasSteerPoint)
                return false;

            direction = _steerPoint - myPosition;
            direction.y = 0f;
            return true;
        }

        private bool IsTargetAbove(Vector3 myPosition, Vector3 targetPosition)
        {
            var feetY = myPosition.y - _npc.PrefabProps.AnimationSystem.RestRootHeight;
            return targetPosition.y > feetY + _targetAboveHeight;
        }

        private bool IsTargetBelow(Vector3 myPosition, Vector3 targetPosition)
        {
            var feetY = myPosition.y - _npc.PrefabProps.AnimationSystem.RestRootHeight;
            return targetPosition.y < feetY - _targetBelowHeight;
        }
    }
}
