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
        [Inject] private readonly NpcNavMeshService _npcNavMeshService;

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
        // HUMANS.MDS: one climb cycle t_Ladder_S0_2_S1 (frames 15-45), down = the same played backwards (aliases R).
        private static readonly string[] _ladderUp = { "T_LADDER_STAND_2_S0", "T_LADDER_S0_2_S1", "T_LADDER_S1_2_STAND" };
        private static readonly string[] _ladderDown = { "T_LADDER_STAND_2_S1", "T_LADDER_S1_2_S0", "T_LADDER_S0_2_STAND" };
        // Fallback if the climb cycle doesn't lift the root bone itself (0.9 climbed a few cycles too many).
        private const float _ladderMetersPerCycle = 1.2f;
        // Unloaded vobs (bridges, platforms) far from the hero have no colliders yet - nobody falls through them there.
        private const float _fallCheckMaxHeroDistance = 40f;

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
        // Ladders: step to the ladder during the first animation, change height only during the climb cycles, step off
        // during the last one (start/end as parts of the whole climb).
        private bool _isLadder;
        private static bool _hasLoggedLadderCycle;
        private Vector3 _ladderColumn;
        private float _ladderRiseStart;
        private float _ladderRiseEnd;
        // The climb animations lift the root bone themselves (pose offset): per animation the NPC's base height, so each
        // one starts where the previous one ended - no double lift ("hiccup"). Null: no usable root lift (plain lerp).
        private float[] _ladderBases;
        private float[] _ladderBlendIns;
        // What's left to the ladder's end after the last animation - spread over the whole climb.
        private float _ladderEndError;
        private const float _ladderMaxEndError = 1.5f;

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
            if (!_hasPreviousPosition || RestHeight <= 0f || !CanMove() || !IsNearHero())
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

        private bool IsNearHero()
        {
            var hero = _gameStateService.GothicVm?.GlobalHero as ZenKit.Daedalus.NpcInstance;
            var heroGo = hero?.GetUserData()?.Go;
            return heroGo != null &&
                   Vector3.Distance(heroGo.transform.position, transform.position) < _fallCheckMaxHeroDistance;
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
            _isLadder = false;
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

            var isUp = ladderEnd.y > ladderStart.y;
            var names = isUp ? _ladderUp : _ladderDown;
            var cycles = Mathf.Max(1, Mathf.RoundToInt(GetLadderClimbHeight(ladderStart, ladderEnd) /
                                                       GetLadderMetersPerCycle()));
            var sequence = new System.Collections.Generic.List<string> { names[0] };
            for (var i = 0; i < cycles; i++)
                sequence.Add(names[1]);
            sequence.Add(names[2]);

            _climbFrom = transform.position;
            _climbTo = ladderEnd + Vector3.up * RestHeight;
            _isLadder = true;
            Vector3 flat;
            if (_npcNavMeshService.TryGetLadderStance(ladderStart, ladderEnd, out var column, out var facing))
            {
                // In front of the rungs, looking at them - up and down at the same spot (like the engine).
                _ladderColumn = new Vector3(column.x, 0f, column.z);
                flat = facing;
            }
            else
            {
                var middle = (ladderStart + ladderEnd) * 0.5f;
                _ladderColumn = new Vector3(middle.x, 0f, middle.z);
                var bottom = isUp ? ladderStart : ladderEnd;
                var top = isUp ? ladderEnd : ladderStart;
                flat = top - bottom;
            }
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(flat);

            _physicsService.DisablePhysicsForNpc(_npc.PrefabProps);
            _npc.PrefabProps.AnimationSystem.StopAllAnimations();
            _npc.Props.BodyState = VmGothicEnums.BodyState.BsClimb;
            StartSequence(State.Climbing, sequence.ToArray());
            // Without the animations (other models): a plain climb at ladder speed.
            if (_stateDuration <= _climbMinSeconds)
            {
                _stateDuration = Mathf.Abs(ladderEnd.y - ladderStart.y) / _ladderSpeed + _ladderExtraSeconds;
                _ladderRiseStart = 0.1f;
                _ladderRiseEnd = 0.9f;
            }
            else
            {
                _ladderRiseStart = Mathf.Clamp01(_sequenceDurations[0] / _stateDuration);
                _ladderRiseEnd = Mathf.Clamp(1f - _sequenceDurations[^1] / _stateDuration, _ladderRiseStart + 0.01f, 1f);
            }
            PrepareLadderBases();

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

            var t = Mathf.Clamp01(_stateTime / _stateDuration);
            if (_isLadder)
            {
                TickLadder(t);
                return;
            }

            // Up first, then forward onto the ledge.
            var rise = Mathf.Clamp01(t / _climbRisePart);
            var forward = Mathf.Clamp01((t - _climbRisePart * 0.5f) / (1f - _climbRisePart * 0.5f));
            var horizontal = Vector3.Lerp(new Vector3(_climbFrom.x, 0f, _climbFrom.z), new Vector3(_climbTo.x, 0f, _climbTo.z), forward);
            transform.position = new Vector3(horizontal.x, Mathf.Lerp(_climbFrom.y, _climbTo.y, rise), horizontal.z);

            if (t >= 1f)
                Finish();
        }

        /// <summary>
        /// Height the climb cycles have to cover: the whole ladder minus what getting on and off lift already.
        /// </summary>
        private float GetLadderClimbHeight(Vector3 ladderStart, Vector3 ladderEnd)
        {
            var height = Mathf.Abs(ladderEnd.y - ladderStart.y);
            var transitions = Mathf.Abs(RootHeightChange(_ladderUp[0])) + Mathf.Abs(RootHeightChange(_ladderUp[2]));
            return transitions < height * 0.5f ? height - transitions : height;
        }

        /// <summary>
        /// One t_Ladder_S0_2_S1 cycle lifts the root this far in HUMANS.MDS (fallback if the track has no root lift).
        /// </summary>
        private float GetLadderMetersPerCycle()
        {
            var perCycle = Mathf.Abs(RootHeightChange(_ladderUp[1]));
            if (!_hasLoggedLadderCycle)
            {
                _hasLoggedLadderCycle = true;
                Logger.Log($"[NpcJumpFall] Ladder cycle lifts {perCycle:F2} m (getting on " +
                           $"{RootHeightChange(_ladderUp[0]):F2}, off {RootHeightChange(_ladderUp[2]):F2}).", LogCat.Ai);
            }
            return perCycle > 0.2f ? perCycle : _ladderMetersPerCycle;
        }

        private float RootHeightChange(string animation) =>
            _animationService.GetRootHeightChange(animation, _npc.Props.MdsNameBase, _npc.Props.MdsNameOverlay);

        /// <summary>
        /// base[i] + firstOffset[i] = base[i-1] + lastOffset[i-1]: every animation continues at the height the previous
        /// one left the body. Starts at the NPC's own height.
        /// </summary>
        private void PrepareLadderBases()
        {
            _ladderBases = null;
            if (_sequence == null || _sequence.Length == 0)
                return;

            var bases = new float[_sequence.Length];
            var blendIns = new float[_sequence.Length];
            var baseY = _climbFrom.y;
            float? previousLast = null;
            for (var i = 0; i < _sequence.Length; i++)
            {
                if (_sequenceDurations[i] <= 0f ||
                    !_animationService.TryGetRootHeightOffsets(_sequence[i], _npc.Props.MdsNameBase,
                        _npc.Props.MdsNameOverlay, out var first, out var last))
                {
                    bases[i] = baseY;
                    continue;
                }

                if (previousLast != null)
                    baseY += previousLast.Value - first;
                bases[i] = baseY;
                blendIns[i] = _animationService.GetTrack(_sequence[i], _npc.Props.MdsNameBase,
                    _npc.Props.MdsNameOverlay)?.BlendIn ?? 0f;
                previousLast = last;
            }

            if (previousLast == null)
                return;
            var endError = _climbTo.y - (baseY + previousLast.Value);
            if (Mathf.Abs(endError) > _ladderMaxEndError)
            {
                Logger.LogWarning($"[NpcJumpFall] Ladder root lift doesn't fit ({endError:F2} m off) - plain climb.",
                    LogCat.Ai);
                return;
            }

            _ladderBases = bases;
            _ladderBlendIns = blendIns;
            _ladderEndError = endError;
        }

        /// <summary>
        /// Base height of the running animation. While it blends in, the pose offset still blends from the previous
        /// animation's - the base moves along, so the body doesn't hop.
        /// </summary>
        private float GetLadderBaseY(float t)
        {
            var index = Mathf.Clamp(_sequenceIndex, 0, _ladderBases.Length - 1);
            var baseY = _ladderBases[index];
            if (index > 0 && _ladderBlendIns[index] > 0f)
                baseY = Mathf.Lerp(_ladderBases[index - 1], baseY, Mathf.Clamp01(_sequenceTime / _ladderBlendIns[index]));
            return baseY + _ladderEndError * t;
        }

        /// <summary>
        /// Onto the ladder (first animation), up/down along it (climb cycles), off it (last animation).
        /// </summary>
        private void TickLadder(float t)
        {
            var fromFlat = new Vector3(_climbFrom.x, 0f, _climbFrom.z);
            var toFlat = new Vector3(_climbTo.x, 0f, _climbTo.z);
            Vector3 horizontal;
            if (t < _ladderRiseStart)
                horizontal = Vector3.Lerp(fromFlat, _ladderColumn, t / _ladderRiseStart);
            else if (t <= _ladderRiseEnd)
                horizontal = _ladderColumn;
            else
                horizontal = Vector3.Lerp(_ladderColumn, toFlat, (t - _ladderRiseEnd) / Mathf.Max(1f - _ladderRiseEnd, 0.001f));

            var rise = Mathf.Clamp01((t - _ladderRiseStart) / (_ladderRiseEnd - _ladderRiseStart));
            var y = _ladderBases != null ? GetLadderBaseY(t) : Mathf.Lerp(_climbFrom.y, _climbTo.y, rise);
            transform.position = new Vector3(horizontal.x, y, horizontal.z);

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

            // Ledges: short; ladders (more than 3 animations) take as long as their climb cycles.
            if (state == State.Climbing && animations.Length <= 3)
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
            // Ladder: off it exactly at its end point (the base height + last pose offset left it mid-air/inside).
            if (_isLadder && _state == State.Climbing)
                transform.position = _climbTo;

            if (_sequence != null && _sequenceIndex >= 0 && _sequenceIndex < _sequence.Length)
                _npc.PrefabProps.AnimationSystem.StopAnimation(_sequence[_sequenceIndex]);
            if (_fallAnimName != null)
                _npc.PrefabProps.AnimationSystem.StopAnimation(_fallAnimName);

            _state = State.None;
            _isLadder = false;
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
