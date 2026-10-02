using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Npc
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableNpcJumpAndFall): NPCs fall off ledges and climb onto ledges like the engine.
    /// - Falling: no ground within STEP_HEIGHT below the feet -> S_FALLDN (S_FALL when deeper), the direction and speed
    ///   of the run are kept (no steering in the air), landing T_FALLDN_2_STAND or T_FALL_2_FALLEN + T_FALLEN_2_STAND.
    ///   Fall damage like the engine: FALLDOWN_DAMAGE per meter above FALLDOWN_HEIGHT (Species.d), minus PROT_FALL.
    /// - Climbing (chasing NPCs only, called by AttackPlayAni): a ledge in front between STEP_HEIGHT and JUMPMID_HEIGHT
    ///   is climbed with the JumpUpLow/JumpUpMid animations - only if the model has them (most monsters don't).
    /// Lives on the NPC (added on demand), not in an AI action: ZS_Attack_Loop clears the AI queue every ~2 s, an action
    /// would leave the NPC hanging in the air.
    /// Not saved - a loaded NPC stands on the ground anyway.
    /// </summary>
    public class NpcJumpFall : MonoBehaviour
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly PhysicsService _physicsService;

        private const float _gravity = 9.81f;
        private const float _maxFallSpeedH = 6f;
        private const float _deepFallHeight = 2f;
        private const float _outOfWorldFallHeight = 100f;
        private const float _groundProbeExtra = 0.1f;
        private const float _ledgeProbeDistance = 0.8f;
        private const float _ledgeStepIn = 0.35f;
        private const float _climbMinSeconds = 0.6f;
        private const float _climbMaxSeconds = 2f;
        private const float _climbRisePart = 0.65f;
        private const float _defaultStepHeight = 0.6f;
        private const float _defaultJumpLow = 1.05f;
        private const float _defaultJumpMid = 2.05f;
        private const int _protFallIndex = 7; // PROT_FALL / DAM_INDEX_FALL
        private const float _ladderSpeed = 1.5f;
        private const float _ladderExtraSeconds = 0.6f;
        private const float _dropProbeDistance = 1f;
        private static readonly string[] _ladderAnimations = { "S_LADDER", "S_JUMPUPMID" };

        private enum State
        {
            None,
            Falling,
            Landing,
            Climbing
        }

        private NpcContainer _npc;
        private State _state;
        private Vector3 _previousPosition;
        private bool _hasPreviousPosition;

        // Falling
        private Vector3 _fallVelocityH;
        private float _fallVelocityY;
        private float _fallStartFeetY;
        private string _fallAnimName;

        // Landing / climbing: a sequence of animations played one after another.
        private string[] _sequence;
        private float[] _sequenceDurations;
        private int _sequenceIndex;
        private float _sequenceTime;
        private float _stateTime;
        private float _stateDuration;
        private Vector3 _climbFrom;
        private Vector3 _climbTo;

        public bool IsBusy => _state != State.None;

        public static NpcJumpFall Get(NpcContainer npc)
        {
            if (npc?.Go == null)
                return null;

            if (!npc.Go.TryGetComponent<NpcJumpFall>(out var jumpFall))
                jumpFall = npc.Go.AddComponent<NpcJumpFall>();
            jumpFall._npc = npc;
            return jumpFall;
        }

        private void Awake()
        {
            this.Inject();
        }

        private void Update()
        {
            if (_npc?.PrefabProps?.AnimationSystem == null || !_configService.Dev.EnableNpcJumpAndFall)
                return;

            switch (_state)
            {
                case State.None:
                    CheckFall();
                    break;
                case State.Falling:
                    TickFall();
                    break;
                case State.Landing:
                    TickSequence();
                    if (_stateTime >= _stateDuration)
                        Finish();
                    break;
                case State.Climbing:
                    TickClimb();
                    break;
            }

            _previousPosition = transform.position;
            _hasPreviousPosition = true;
        }

        private float RestHeight => _npc.PrefabProps.AnimationSystem.RestRootHeight;
        private int Guild => _npc.Instance.Guild <= (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM
            ? (int)VmGothicEnums.Guild.GIL_HUMAN
            : _npc.Instance.Guild;

        private float StepHeight => GuildCm(_gameStateService.GuildValues?.GetStepHeight(Guild), _defaultStepHeight);
        private float JumpLowHeight => GuildCm(_gameStateService.GuildValues?.GetJumpLowHeight(Guild), _defaultJumpLow);
        private float JumpMidHeight => GuildCm(_gameStateService.GuildValues?.GetJumpMidHeight(Guild), _defaultJumpMid);

        private static float GuildCm(int? cm, float fallback) => cm is > 0 and < 10000 ? cm.Value / 100f : fallback;

        private bool CanMove()
        {
            var bodyState = _npc.Props.BodyState;
            if (bodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious
                or VmGothicEnums.BodyState.BsSwim or VmGothicEnums.BodyState.BsDive)
                return false;

            // Flying monsters (both water depths 999999) and NPCs in water don't fall.
            var guildValues = _gameStateService.GuildValues;
            if (guildValues != null && guildValues.GetWaterDepthChest(Guild) >= 999999)
                return false;
            return _npc.Vob?.AiHuman == null || _npc.Vob.AiHuman.WaterLevel == (int)ZenGineConst.WaterLevel.Normal;
        }

        private void CheckFall()
        {
            if (!_hasPreviousPosition || RestHeight <= 0f || !CanMove())
                return;

            // Only NPCs that move walk off a ledge (an idle NPC on a vob that isn't loaded yet must not drop).
            var moved = transform.position - _previousPosition;
            moved.y = 0f;
            if (moved.sqrMagnitude < 0.0001f)
                return;

            var probe = RestHeight + StepHeight + _groundProbeExtra;
            if (Physics.Raycast(transform.position, Vector3.down, probe, 1 << Constants.DefaultLayer,
                    QueryTriggerInteraction.Ignore))
                return;

            StartFall(moved / Mathf.Max(Time.deltaTime, 0.001f));
        }

        private void StartFall(Vector3 velocityH)
        {
            _state = State.Falling;
            _fallVelocityH = Vector3.ClampMagnitude(velocityH, _maxFallSpeedH);
            _fallVelocityY = 0f;
            _fallStartFeetY = transform.position.y - RestHeight;
            _npc.Props.BodyState = VmGothicEnums.BodyState.BsFall;

            _physicsService.DisablePhysicsForNpc(_npc.PrefabProps);
            _npc.PrefabProps.AnimationSystem.StopAllAnimations();
            _fallAnimName = "S_FALLDN";
            PlayIfExists(_fallAnimName);

            Logger.Log($"[NpcJumpFall] {_npc.Go.name} falls (speed {_fallVelocityH.magnitude:F1} m/s).", LogCat.Ai);
        }

        private void TickFall()
        {
            var dt = Time.deltaTime;
            _fallVelocityY -= _gravity * dt;

            var position = transform.position;
            var moveH = _fallVelocityH * dt;
            if (moveH.sqrMagnitude > 0f && Physics.Raycast(position, moveH.normalized, moveH.magnitude + 0.3f,
                    1 << Constants.DefaultLayer, QueryTriggerInteraction.Ignore))
            {
                // Against a wall: slide down it.
                _fallVelocityH = Vector3.zero;
                moveH = Vector3.zero;
            }

            var moveY = _fallVelocityY * dt;
            var probe = RestHeight - moveY + 0.05f;
            if (Physics.Raycast(position + moveH, Vector3.down, out var hit, probe, 1 << Constants.DefaultLayer,
                    QueryTriggerInteraction.Ignore))
            {
                transform.position = new Vector3(position.x + moveH.x, hit.point.y + RestHeight, position.z + moveH.z);
                Land(_fallStartFeetY - hit.point.y);
                return;
            }

            transform.position = position + moveH + Vector3.up * moveY;

            var fallen = _fallStartFeetY - (transform.position.y - RestHeight);
            if (fallen > _deepFallHeight && _fallAnimName != "S_FALL")
            {
                _npc.PrefabProps.AnimationSystem.StopAnimation(_fallAnimName);
                _fallAnimName = "S_FALL";
                PlayIfExists(_fallAnimName);
            }
            else if (!_npc.PrefabProps.AnimationSystem.IsPlaying(_fallAnimName))
            {
                // Npc_ClearAIQueue stops all animations every ~2 s.
                PlayIfExists(_fallAnimName);
            }

            if (fallen > _outOfWorldFallHeight)
            {
                Logger.LogWarning($"[NpcJumpFall] {_npc.Go.name} fell {fallen:F0} m without ground - stopped.", LogCat.Ai);
                Finish();
            }
        }

        private void Land(float height)
        {
            _npc.PrefabProps.AnimationSystem.StopAnimation(_fallAnimName);
            var isDeep = height > _deepFallHeight;
            StartSequence(State.Landing, isDeep
                ? new[] { "T_FALL_2_FALLEN", "T_FALLEN_2_STAND" }
                : new[] { "T_FALLDN_2_STAND" });

            ApplyFallDamage(height);
            Logger.Log($"[NpcJumpFall] {_npc.Go.name} landed after {height:F1} m.", LogCat.Ai);
        }

        /// <summary>
        /// Engine: FALLDOWN_DAMAGE HP for every meter above FALLDOWN_HEIGHT (cm), minus PROT_FALL.
        /// </summary>
        private void ApplyFallDamage(float height)
        {
            var guildValues = _gameStateService.GuildValues;
            if (guildValues == null)
                return;

            var safeHeight = guildValues.GetFallDownHeight(Guild) / 100f;
            var damagePerMeter = guildValues.GetFallDownDamage(Guild);
            if (safeHeight <= 0f || damagePerMeter <= 0 || height <= safeHeight)
                return;

            var damage = Mathf.RoundToInt((height - safeHeight) * damagePerMeter) - _npc.Vob.GetProtection(_protFallIndex);
            if (damage > 0)
                GlobalEventDispatcher.FallDamage.Invoke(_npc, damage);
        }

        /// <summary>
        /// A chasing NPC runs into a ledge: climb it if it's higher than a step and not higher than JUMPMID_HEIGHT.
        /// </summary>
        public bool TryStartClimb(Vector3 direction)
        {
            if (!_configService.Dev.EnableNpcJumpAndFall || IsBusy || RestHeight <= 0f || !CanMove())
                return false;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return false;
            direction.Normalize();

            var mask = 1 << Constants.DefaultLayer;
            var feetY = transform.position.y - RestHeight;
            var position = transform.position;
            var stepHeight = StepHeight;
            var jumpMid = JumpMidHeight;

            // 1. Something blocks the way above step height.
            var kneeOrigin = new Vector3(position.x, feetY + stepHeight + 0.05f, position.z);
            if (!Physics.Raycast(kneeOrigin, direction, out var wallHit, _ledgeProbeDistance, mask, QueryTriggerInteraction.Ignore))
                return false;

            // 2. Its top is reachable.
            var topProbe = wallHit.point + direction * _ledgeStepIn;
            topProbe.y = feetY + jumpMid + 0.3f;
            if (!Physics.Raycast(topProbe, Vector3.down, out var topHit, jumpMid + 0.3f - stepHeight, mask,
                    QueryTriggerInteraction.Ignore))
                return false;

            var height = topHit.point.y - feetY;
            if (height <= stepHeight || height > jumpMid || topHit.normal.y < 0.7f)
                return false;

            // 3. Room to stand up there.
            if (Physics.Raycast(topHit.point + Vector3.up * 0.05f, Vector3.up, RestHeight * 1.8f, mask,
                    QueryTriggerInteraction.Ignore))
                return false;

            var sequence = height <= JumpLowHeight
                ? new[] { "T_STAND_2_JUMPUPLOW", "S_JUMPUPLOW", "T_JUMPUPLOW_2_STAND" }
                : new[] { "T_STAND_2_JUMPUPMID", "S_JUMPUPMID", "T_JUMPUPMID_2_STAND" };
            if (!HasTrack(sequence[0]))
                return false;

            _climbFrom = position;
            _climbTo = topHit.point + Vector3.up * RestHeight;
            _physicsService.DisablePhysicsForNpc(_npc.PrefabProps);
            _npc.PrefabProps.AnimationSystem.StopAllAnimations();
            transform.rotation = Quaternion.LookRotation(direction);
            _npc.Props.BodyState = VmGothicEnums.BodyState.BsJump;
            StartSequence(State.Climbing, sequence);

            Logger.Log($"[NpcJumpFall] {_npc.Go.name} climbs a {height:F2} m ledge.", LogCat.Ai);
            return true;
        }

        /// <summary>
        /// The NavMesh path goes over a ladder link: climb it from start to end (up or down).
        /// </summary>
        public bool TryStartLadder(Vector3 ladderStart, Vector3 ladderEnd)
        {
            if (!_configService.Dev.EnableNpcJumpAndFall || IsBusy || RestHeight <= 0f || !CanMove())
                return false;

            var animation = System.Array.Find(_ladderAnimations, HasTrack);
            _climbFrom = transform.position;
            _climbTo = ladderEnd + Vector3.up * RestHeight;

            var flat = ladderEnd - ladderStart;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(flat);

            _physicsService.DisablePhysicsForNpc(_npc.PrefabProps);
            _npc.PrefabProps.AnimationSystem.StopAllAnimations();
            _npc.Props.BodyState = VmGothicEnums.BodyState.BsClimb;
            StartSequence(State.Climbing, animation != null ? new[] { animation } : System.Array.Empty<string>());
            _stateDuration = Mathf.Abs(ladderEnd.y - ladderStart.y) / _ladderSpeed + _ladderExtraSeconds;

            Logger.Log($"[NpcJumpFall] {_npc.Go.name} takes a ladder ({ladderStart.y:F1} -> {ladderEnd.y:F1}).", LogCat.Ai);
            return true;
        }

        /// <summary>
        /// Ground below the next meter in that direction, not deeper than FALLDOWN_HEIGHT: a chasing NPC may jump down
        /// to a target below instead of stopping at the edge.
        /// </summary>
        public bool IsSafeDropAhead(Vector3 direction)
        {
            if (!_configService.Dev.EnableNpcJumpAndFall || RestHeight <= 0f)
                return false;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return false;

            var safeHeight = (_gameStateService.GuildValues?.GetFallDownHeight(Guild) ?? 0) / 100f;
            if (safeHeight <= 0f)
                return false;

            var origin = transform.position + direction.normalized * _dropProbeDistance;
            return Physics.Raycast(origin, Vector3.down, RestHeight + safeHeight, 1 << Constants.DefaultLayer,
                QueryTriggerInteraction.Ignore);
        }

        private void TickClimb()
        {
            TickSequence();

            // Up first, then forward onto the ledge.
            var t = Mathf.Clamp01(_stateTime / _stateDuration);
            var rise = Mathf.Clamp01(t / _climbRisePart);
            var forward = Mathf.Clamp01((t - _climbRisePart * 0.5f) / (1f - _climbRisePart * 0.5f));
            var horizontal = Vector3.Lerp(new Vector3(_climbFrom.x, 0f, _climbFrom.z), new Vector3(_climbTo.x, 0f, _climbTo.z), forward);
            transform.position = new Vector3(horizontal.x, Mathf.Lerp(_climbFrom.y, _climbTo.y, rise), horizontal.z);

            if (t >= 1f)
                Finish();
        }

        private void StartSequence(State state, string[] animations)
        {
            _state = state;
            _sequence = animations;
            _sequenceDurations = new float[animations.Length];
            _stateDuration = 0f;
            for (var i = 0; i < animations.Length; i++)
            {
                _sequenceDurations[i] = HasTrack(animations[i])
                    ? _npc.PrefabProps.AnimationSystem.GetAnimationDuration(animations[i])
                    : 0f;
                _stateDuration += _sequenceDurations[i];
            }

            if (state == State.Climbing)
                _stateDuration = Mathf.Clamp(_stateDuration, _climbMinSeconds, _climbMaxSeconds);

            _sequenceIndex = -1;
            _sequenceTime = 0f;
            _stateTime = 0f;
            NextInSequence();
        }

        private void TickSequence()
        {
            _stateTime += Time.deltaTime;
            _sequenceTime += Time.deltaTime;
            if (_sequenceIndex < _sequence.Length && _sequenceTime >= _sequenceDurations[_sequenceIndex])
                NextInSequence();
        }

        private void NextInSequence()
        {
            if (_sequenceIndex >= 0 && _sequenceIndex < _sequence.Length)
                _npc.PrefabProps.AnimationSystem.StopAnimation(_sequence[_sequenceIndex]);

            _sequenceIndex++;
            _sequenceTime = 0f;
            while (_sequenceIndex < _sequence.Length && _sequenceDurations[_sequenceIndex] <= 0f)
                _sequenceIndex++;

            if (_sequenceIndex < _sequence.Length)
                _npc.PrefabProps.AnimationSystem.PlayAnimation(_sequence[_sequenceIndex]);
        }

        private void Finish()
        {
            if (_sequence != null && _sequenceIndex >= 0 && _sequenceIndex < _sequence.Length)
                _npc.PrefabProps.AnimationSystem.StopAnimation(_sequence[_sequenceIndex]);
            if (_fallAnimName != null)
                _npc.PrefabProps.AnimationSystem.StopAnimation(_fallAnimName);

            _state = State.None;
            _sequence = null;
            _fallAnimName = null;
            if (_npc.Props.BodyState is VmGothicEnums.BodyState.BsFall or VmGothicEnums.BodyState.BsJump
                or VmGothicEnums.BodyState.BsClimb)
                _npc.Props.BodyState = VmGothicEnums.BodyState.BsStand;
            _physicsService.EnablePhysicsForNpc(_npc.PrefabProps);
            _npc.PrefabProps.AnimationSystem.PlayIdleAnimation();
        }

        private void PlayIfExists(string animationName)
        {
            if (HasTrack(animationName))
                _npc.PrefabProps.AnimationSystem.PlayAnimation(animationName);
        }

        private bool HasTrack(string animationName)
        {
            return _animationService.GetTrack(animationName, _npc.Props.MdsNameBase, _npc.Props.MdsNameOverlay) != null;
        }
    }
}
