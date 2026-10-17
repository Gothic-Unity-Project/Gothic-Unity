using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Logger = Gothic.Core.Logging.Logger;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// Basically PlayAni with special Attack handling.
    /// </summary>
    public class AttackPlayAni : PlayAni
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly Gothic.Core.Services.Meshes.ParticleService _particleService;
        [Inject] private readonly NpcNavMeshService _npcNavMeshService;

        private FightAiMove _move => (FightAiMove)Action.Int0;
        private NpcContainer _enemy => Action.Instance0.GetUserData();
        private Transform _enemyTransform => _enemy.Go.transform;
        private bool _comboWindowLogged;
        private bool _hasHitFired;
        private bool _isRangedFollowUp;
        private bool _isRunAttackPlanned;
        private bool _isRunAttackActive;
        private float _runAttackHitTime;
        private float _runAttackSpeed;
        private bool _isRunKeptUnderStrike;
        private const float _runAttackChance = 0.5f;
        private const float _runAttackLeadDistance = 0.6f;
        private const float _runAttackHitFraction = 0.5f;
        private const float _minRunAttackSeconds = 0.4f;
        // Run speed when the run animation reports none (m/s), and how close the lunge may carry the NPC.
        private const float _fallbackRunAttackSpeed = 3f;
        private const float _minRunAttackDistance = 0.8f;
        private const string _bowReloadAnimName = "t_BowReload";
        private const string _crossbowReloadAnimName = "t_CBowReload";
        private const string _spellShootToStandAnimName = "t_FBTShoot_2_Stand";
        private const float _minRangedFollowUpSeconds = 0.5f;

        /// <summary>
        /// A melee/magic swing that hasn't reached its hit frame yet.
        /// </summary>
        public bool IsSwingInProgress =>
            !IsFinishedFlag && !_hasHitFired && _move is FightAiMove.Attack or FightAiMove.AttackSide;
        private string _activeTurnAnimName;
        private const float _turnThresholdDeg = 10f;

        private float _chaseTimer;
        private float _heroStopTimer;
        private Vector3 _previousHeroPos;
        private bool _firstRunTick = true;
        private const float _chaseGiveUpDuration = 10f;
        private const float _heroStopResetDelay = 2f;
        private const float _heroRunSpeedThreshold = 2f;

        // Swimming (DeveloperConfig.EnableNpcWater): no attacks, only swimming after the target.
        private const string _swimIdleAnimName = "S_SWIM";
        private const string _swimForwardAnimName = "S_SWIMF";
        private const float _swimWaitSeconds = 0.5f;
        private string _swimAnimName;

        // NavMesh steering while running (DeveloperConfig.EnableNpcNavMesh), recalculated a few times per second.
        private const float _pathUpdateInterval = 0.2f;
        private const float _unreachableStopDistance = 0.6f;
        private const float _runStepSeconds = 0.5f;
        private float _runStepTime;
        private float _nextPathUpdateTime;
        private bool _hasSteerPoint;
        private Vector3 _steerPoint;
        private bool _isTargetReachable = true;


        public AttackPlayAni(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            if (Gothic.Core.Services.Npc.NpcHelperService.IsDown(_enemy))
            {
                IsFinishedFlag = true;
                return;
            }

            // Falling/climbing (NpcJumpFall): no fight move in the air - the next AI_Attack loop continues afterwards.
            if (NpcJumpFall.Get(NpcContainer)?.IsBusy == true)
            {
                IsFinishedFlag = true;
                return;
            }

            if (IsSwimming())
            {
                StartSwimming();
                return;
            }
            base.Start();

            // DeveloperConfig.EnableNpcRunAttack: this run either ends in a strike out of the run or, like before,
            // stops in range and the next AI_Attack strikes from a stand.
            _isRunAttackPlanned = _move == FightAiMove.Run && _configService.Dev.EnableNpcRunAttack &&
                                  Random.value < _runAttackChance;
        }

        private bool IsSwimming()
        {
            return _configService.Dev.EnableNpcWater &&
                   Vob.AiHuman?.WaterLevel == (int)ZenGineConst.WaterLevel.Chest;
        }

        /// <summary>
        /// In water the engine has no fight moves: a swimming monster/NPC only follows its target (until it stands on
        /// ground again). Run = swim towards it, every other move = tread water facing it.
        /// </summary>
        private void StartSwimming()
        {
            _swimAnimName = _move == FightAiMove.Run ? _swimForwardAnimName : _swimIdleAnimName;
            if (!PrefabProps.AnimationSystem.IsPlaying(_swimAnimName) &&
                !PrefabProps.AnimationSystem.PlayAnimation(_swimAnimName))
            {
                IsFinishedFlag = true;
                return;
            }
            ActionEndEventTime = _move == FightAiMove.Run
                ? PrefabProps.AnimationSystem.GetAnimationDuration(_swimAnimName)
                : _swimWaitSeconds;
        }

        public override void Tick()
        {
            if (_swimAnimName != null)
            {
                SwimTick();
                return;
            }

            base.Tick();

            if (IsFinishedFlag)
            {
                // The attack played to its end - the next one starts a new combo.
                Props.AttackComboLength = 0;
                StopTurnAnimation();
                // Magic/ranged animations have no DEF_OPT_FRAME, so HasComboWindowOpened never fires.
                // Fire the hit at animation end instead.
                // DeveloperConfig.EnableNpcHitAtOptimalFrame: a melee animation without DEF_OPT_FRAME/DEF_WINDOW hits
                // at its end too - wolves' bites never checked for a hit.
                if (!_hasHitFired && ((VmGothicEnums.WeaponState)Vob.FightMode is
                    VmGothicEnums.WeaponState.Mage or VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow ||
                    _configService.Dev.EnableNpcHitAtOptimalFrame))
                {
                    _hasHitFired = true;
                    TryFireHit();
                }

                if (!_isRangedFollowUp && TryStartRangedFollowUp())
                    return;
                return;
            }

            // DeveloperConfig.EnableNpcHitAtOptimalFrame: like the engine, the hit lands at DEF_OPT_FRAME.
            if (!_hasHitFired && _configService.Dev.EnableNpcHitAtOptimalFrame &&
                PrefabProps.AnimationSystem.HasOptimalFrameReached(Action.String0))
            {
                _hasHitFired = true;
                TryFireHit();
            }

            // Combo chaining: once the DEF_WINDOW frame is reached, cut this animation short so the
            // next queued attack starts immediately — exactly like Gothic's original combo system.
            // The last attack in a sequence has nothing queued, so it plays to full completion.
            if (PrefabProps.AnimationSystem.HasComboWindowOpened)
            {
                // Auto-register hit at the attack frame (replaces bone collider detection, temporary debug aid).
                if (!_hasHitFired)
                {
                    _hasHitFired = true;
                    TryFireHit();
                }

                // DeveloperConfig.EnableNpcComboLimit: like the engine (OpenGothic Pose::continueCombo), a combo has as
                // many follow-up hits as the animation has DEF_WINDOW pairs - then it breaks and the attack plays to its
                // end. ZS_MM_Attack_Loop queues AI_Attack every loop: a lurker chained 4 bites a second forever.
                var isComboLeft = !_configService.Dev.EnableNpcComboLimit ||
                                  Props.AttackComboLength < PrefabProps.AnimationSystem.ComboWindowCount;
                if (Props.AnimationQueue.Count > 0 && isComboLeft)
                {
                    Props.AttackComboLength++;
                    Logger.LogWarning($"[Combo] {NpcInstance.GetName(NpcNameSlot.Slot0)} cutting anim early → next hit (queue={Props.AnimationQueue.Count}, combo {Props.AttackComboLength}/{PrefabProps.AnimationSystem.ComboWindowCount})", LogCat.Animation);
                    // Stop the current track so IsAlreadyPlaying won't block the next attack from starting fresh.
                    PrefabProps.AnimationSystem.StopAnimation(Action.String0);
                    IsFinishedFlag = true;
                    return;
                }

                // Enemy died during this attack (loop returned LOOP_END) — cut at combo window
                // instead of letting the full animation play out.
                if (Props.CurrentLoopState == NpcProperties.LoopState.End)
                {
                    Logger.LogWarning($"[Combo] {NpcInstance.GetName(NpcNameSlot.Slot0)} enemy down — cutting at combo window", LogCat.Animation);
                    PrefabProps.AnimationSystem.StopAnimation(Action.String0);
                    IsFinishedFlag = true;
                    return;
                }

                if (!_comboWindowLogged)
                {
                    _comboWindowLogged = true;
                    Logger.Log($"[Combo] {NpcInstance.GetName(NpcNameSlot.Slot0)} window open, queue empty - last anim plays to end", LogCat.Animation);
                }
            }

            switch (_move)
            {
                case FightAiMove.Run:
                    if (_isRunAttackActive)
                        RunAttackTick();
                    else
                        RunTick();
                    break;
                case FightAiMove.Strafe:
                    StrafeTick();
                    break;
                default:
                    HandleCombatRotation();
                    break;
            }
        }

        private void SwimTick()
        {
            // AbstractAnimationAction's timer (ActionEndEventTime) - PlayAni's blend-out check is for String0.
            base.Tick();
            if (IsFinishedFlag)
                return;

            if (_move == FightAiMove.Run)
            {
                RunTick();
                return;
            }

            var direction = _enemyTransform.position - NpcGo.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
                return;

            var guild = NpcInstance.Guild <= (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM ? (int)VmGothicEnums.Guild.GIL_HUMAN : NpcInstance.Guild;
            NpcGo.transform.rotation = Quaternion.RotateTowards(NpcGo.transform.rotation,
                Quaternion.LookRotation(direction), Time.deltaTime * GameStateService.GuildValues.GetTurnSpeed(guild));
        }

        private void HandleCombatRotation()
        {
            var myPos = NpcGo.transform.position;
            var targetPos = _enemyTransform.position;
            var direction = new Vector3(targetPos.x - myPos.x, 0, targetPos.z - myPos.z);

            if (direction.sqrMagnitude < 0.001f)
                return;

            var angle = Vector3.SignedAngle(NpcGo.transform.forward, direction.normalized, Vector3.up);
            var guild = NpcInstance.Guild <= (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM ? (int)VmGothicEnums.Guild.GIL_HUMAN : NpcInstance.Guild;
            var turnSpeed = GameStateService.GuildValues.GetTurnSpeed(guild);

            if (Mathf.Abs(angle) > _turnThresholdDeg)
            {
                var desiredAnim = AnimationService.GetAnimationName(
                    angle < 0 ? VmGothicEnums.AnimationType.RotL : VmGothicEnums.AnimationType.RotR,
                    NpcContainer);

                if (_activeTurnAnimName != desiredAnim)
                {
                    StopTurnAnimation();
                    _activeTurnAnimName = desiredAnim;
                    PrefabProps.AnimationSystem.PlayAnimation(_activeTurnAnimName);
                }

                NpcGo.transform.rotation = Quaternion.RotateTowards(
                    NpcGo.transform.rotation,
                    Quaternion.LookRotation(direction),
                    Time.deltaTime * turnSpeed);
            }
            else
                StopTurnAnimation();
        }

        private void StopTurnAnimation()
        {
            if (_activeTurnAnimName == null)
                return;
            PrefabProps.AnimationSystem.StopAnimation(_activeTurnAnimName);
            _activeTurnAnimName = null;
        }

        private void RunTick()
        {
            // Like the engine's AI_Attack (one step per loop): hand control back to the state loop now and then while
            // running, so ZS_*_Attack_Loop can check "the target ran > 10 s" during the chase (it gives up, says
            // $RUNCOWARD, T_WARN in _End). Before, the loop only ran again on arrival - orcs chased all the way and
            // then put their weapon away right next to the hero. The run animation keeps playing into the next step.
            _runStepTime += Time.deltaTime;
            if (_runStepTime >= _runStepSeconds && Props.AnimationQueue.Count == 0)
            {
                IsFinishedFlag = true;
                return;
            }

            var myPosition = NpcContainer.Go.transform.position;
            var targetPosition = _enemyTransform.position;

            var myPositionH = new Vector3(myPosition.x, 0, myPosition.z);
            var targetPositionH = new Vector3(targetPosition.x, 0, targetPosition.z);
            var toTarget = targetPositionH - myPositionH;

            var arrivalThreshold = _configService.Dev.NpcAttackArrivalThreshold;
            // Attack.GetDistance() subtracts _npcMonsterVolumina (0.3m) from actual distance.
            // To land exactly at attackRange when the run ends, stop at weaponReach + 0.3m.
            // Minimum 1.8m so guild-value edge cases (0 range) don't collapse to 0 stop distance.
            const float npcVoluminaOffset = 0.3f;
            const float minStopDistance = 1.11f;
            var stopDistance = Mathf.Max(GetWeaponReach() + npcVoluminaOffset, minStopDistance);

            // The strike out of the run starts a bit before the stop distance - the move attack lunges forward.
            if (_isRunAttackPlanned && toTarget.magnitude <= stopDistance + _runAttackLeadDistance &&
                TryStartRunAttack())
                return;

            // Already close enough — stop immediately.
            if (toTarget.magnitude <= stopDistance)
            {
                StopRunning();
                return;
            }

            // Run toward a point at stopDistance from the enemy. Each attacker comes from its own
            // direction so they naturally spread around the target instead of stacking.
            var approachTarget = toTarget.sqrMagnitude > 0.001f
                ? targetPositionH - toTarget.normalized * Mathf.Max(0f, stopDistance - arrivalThreshold)
                : targetPositionH;

            var distance = Vector3.Distance(myPositionH, approachTarget);

            if (distance <= arrivalThreshold)
            {
                StopRunning();
                return;
            }

            // Rotate toward actual enemy while running (not toward the offset approach point) - or along the NavMesh path
            // around obstacles. An unreachable enemy (on a rock, down a cliff): stop at the closest point, don't jump.
            var runDirection = toTarget;
            var hasSteerDirection = TryGetSteerDirection(myPosition, targetPosition, out var steerDirection);

            // The enemy stands higher (and the NavMesh has no way up): climb the ledge in front like the engine does.
            if ((!hasSteerDirection || !_isTargetReachable) && IsTargetAbove(myPosition, targetPosition) &&
                NpcJumpFall.Get(NpcContainer)?.TryStartClimb(toTarget) == true)
            {
                IsFinishedFlag = true;
                return;
            }

            // The path goes over a ladder (NavMesh link) - climb it.
            if (hasSteerDirection && _npcNavMeshService.TryGetLadder(myPosition, _steerPoint, out var ladderStart,
                    out var ladderEnd) && NpcJumpFall.Get(NpcContainer)?.TryStartLadder(ladderStart, ladderEnd) == true)
            {
                IsFinishedFlag = true;
                return;
            }

            // The enemy is below and can't be reached on the NavMesh: jump down if the drop is survivable (engine).
            var isDroppingDown = hasSteerDirection && !_isTargetReachable && IsTargetBelow(myPosition, targetPosition) &&
                                 NpcJumpFall.Get(NpcContainer)?.IsSafeDropAhead(toTarget) == true;

            if (hasSteerDirection && !isDroppingDown)
            {
                if (!_isTargetReachable && steerDirection.magnitude < _unreachableStopDistance)
                {
                    StopRunning();
                    return;
                }
                runDirection = steerDirection;
            }

            var guild = NpcInstance.Guild <= (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM ? (int)VmGothicEnums.Guild.GIL_HUMAN : NpcInstance.Guild;
            var turnSpeed = GameStateService.GuildValues.GetTurnSpeed(guild);
            if (runDirection.sqrMagnitude > 0.0001f)
            {
                NpcGo.transform.rotation = Quaternion.RotateTowards(
                    NpcGo.transform.rotation,
                    Quaternion.LookRotation(runDirection),
                    Time.deltaTime * turnSpeed);
            }

            // Give-up: track hero speed via position delta. If hero is running, accumulate
            // _chaseTimer. Only reset it after hero has been stationary for 2s.
            var heroGo = ((NpcInstance)GameStateService.GothicVm.GlobalHero).GetUserData().Go;
            var heroPos = heroGo.transform.position;

            if (_firstRunTick)
            {
                _previousHeroPos = heroPos;
                _firstRunTick = false;
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

            if (_chaseTimer >= _chaseGiveUpDuration)
            {
                Logger.LogWarning($"[AttackPlayAni] {NpcInstance.GetName(NpcNameSlot.Slot0)}: hero ran away — giving up after {_chaseTimer:F1}s", LogCat.Fight);
                PrefabProps.AnimationSystem.StopAllAnimations();
                Props.AnimationQueue.Clear();
                Props.CurrentLoopState = NpcProperties.LoopState.End;
                IsFinishedFlag = true;
            }
        }

        /// <summary>
        /// The shot itself is one frame (s_BowShoot/s_CBowShoot/s_FBTShoot) - finishing there let the next attack
        /// start at once: bursts of arrows/spells. Like the engine, the reload (t_BowReload/t_CBowReload) or the
        /// hands coming down after a spell (t_FBTShoot_2_Stand) belong to the same attack.
        /// </summary>
        private bool TryStartRangedFollowUp()
        {
            if (!_hasHitFired || _move is not (FightAiMove.Attack or FightAiMove.AttackSide))
                return false;

            var followUp = (VmGothicEnums.WeaponState)Vob.FightMode switch
            {
                VmGothicEnums.WeaponState.Bow => _bowReloadAnimName,
                VmGothicEnums.WeaponState.CBow => _crossbowReloadAnimName,
                VmGothicEnums.WeaponState.Mage => _spellShootToStandAnimName,
                _ => null
            };
            if (followUp == null || !PrefabProps.AnimationSystem.PlayAnimation(followUp))
                return false;

            _isRangedFollowUp = true;
            IsFinishedFlag = false;
            ActionEndEventTime = ActionTime + Mathf.Max(PrefabProps.AnimationSystem.GetAnimationDuration(followUp),
                _minRangedFollowUpSeconds);
            return true;
        }

        /// <summary>
        /// t_<weapon>AttackMove - the engine's strike while running (humans: fist/1h, orcs: 2h, wolves/scavengers/... fist).
        /// Missing for this weapon/model (e.g. humans with 2h): false, the run stops as before.
        /// </summary>
        private bool TryStartRunAttack()
        {
            _isRunAttackPlanned = false;
            var prefix = (VmGothicEnums.WeaponState)Vob.FightMode switch
            {
                VmGothicEnums.WeaponState.NoWeapon or VmGothicEnums.WeaponState.Fist => "Fist",
                VmGothicEnums.WeaponState.W1H => "1h",
                VmGothicEnums.WeaponState.W2H => "2h",
                _ => null
            };
            if (prefix == null)
                return false;

            var animName = $"t_{prefix}AttackMove";
            // The run's root motion moved the NPC - the strike has none, so it keeps the run speed (RunAttackTick).
            // The run isn't stopped: a strike on a higher layer plays over it - the legs keep running and the run's
            // root motion moves the NPC. A strike on the run's layer blends it out - then LungeForward() moves it.
            var runSpeed = PrefabProps.AnimationSystem.GetMovementSpeed(Action.String0);
            if (!PrefabProps.AnimationSystem.PlayAnimation(animName))
                return false;
            _isRunKeptUnderStrike = PrefabProps.AnimationSystem.IsPlaying(Action.String0) &&
                                    !PrefabProps.AnimationSystem.IsAnimationBlendingOut(Action.String0);

            var duration = Mathf.Max(PrefabProps.AnimationSystem.GetAnimationDuration(animName), _minRunAttackSeconds);
            _isRunAttackActive = true;
            // An attack animation with its own root motion moves the NPC already.
            _runAttackSpeed = _isRunKeptUnderStrike || PrefabProps.AnimationSystem.GetMovementSpeed(animName) > 0f ? 0f
                : runSpeed > 0f ? runSpeed : _fallbackRunAttackSpeed;
            ActionEndEventTime = ActionTime + duration;
            _runAttackHitTime = ActionTime + duration * _runAttackHitFraction;
            Logger.Log($"[AttackPlayAni] {NpcInstance.GetName(NpcNameSlot.Slot0)} strikes out of the run ({animName}, " +
                       $"run kept {_isRunKeptUnderStrike}, lunge {_runAttackSpeed:F1} m/s)",
                LogCat.Fight);
            return true;
        }

        /// <summary>
        /// The strike out of the run keeps running forward until the animation ends (engine), only not into the target.
        /// </summary>
        private void RunAttackTick()
        {
            HandleCombatRotation();
            LungeForward();
            if (_hasHitFired || ActionTime < _runAttackHitTime)
                return;

            _hasHitFired = true;
            TryFireHit();
        }

        private void LungeForward()
        {
            if (_enemyTransform == null)
                return;

            var toTarget = _enemyTransform.position - NpcGo.transform.position;
            toTarget.y = 0f;
            if (toTarget.magnitude <= _minRunAttackDistance)
            {
                // Arrived - the run under the strike stops, the strike finishes from a stand.
                if (_isRunKeptUnderStrike)
                {
                    _isRunKeptUnderStrike = false;
                    PrefabProps.AnimationSystem.StopAnimation(Action.String0);
                }
                return;
            }
            if (_runAttackSpeed <= 0f)
                return;

            var forward = NpcGo.transform.forward;
            forward.y = 0f;
            var step = Mathf.Min(_runAttackSpeed * Time.deltaTime, toTarget.magnitude - _minRunAttackDistance);
            PrefabProps.AnimationSystem.MoveWithWallCollision(forward.normalized * step);
        }

        /// <summary>
        /// During the reload the one-frame shot animation is already blending out - that must not end the attack.
        /// </summary>
        protected override void AnimationEnd()
        {
            if ((_isRangedFollowUp || _isRunAttackActive) && ActionTime < ActionEndEventTime)
                return;
            // The run under the strike would keep moving the NPC during the next action (e.g. a strike from a stand).
            if (_isRunKeptUnderStrike)
            {
                _isRunKeptUnderStrike = false;
                PrefabProps.AnimationSystem.StopAnimation(Action.String0);
            }
            base.AnimationEnd();
        }

        private bool IsTargetAbove(Vector3 myPosition, Vector3 targetPosition)
        {
            var feetY = myPosition.y - PrefabProps.AnimationSystem.RestRootHeight;
            return targetPosition.y > feetY + 0.5f;
        }

        private bool IsTargetBelow(Vector3 myPosition, Vector3 targetPosition)
        {
            var feetY = myPosition.y - PrefabProps.AnimationSystem.RestRootHeight;
            return targetPosition.y < feetY - 0.8f;
        }

        /// <summary>
        /// End of a run move. A swimmer treads water instead of dropping into the rest pose.
        /// </summary>
        private void StopRunning()
        {
            if (_swimAnimName != null)
            {
                PrefabProps.AnimationSystem.StopAnimation(_swimForwardAnimName);
                PrefabProps.AnimationSystem.PlayAnimation(_swimIdleAnimName);
            }
            else
            {
                PrefabProps.AnimationSystem.StopAllAnimations();
            }
            IsFinishedFlag = true;
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
                _hasSteerPoint = _npcNavMeshService.TryGetSteerPoint(NpcContainer, targetPosition, out _steerPoint,
                    out _isTargetReachable);
            }

            if (!_hasSteerPoint)
                return false;

            direction = _steerPoint - myPosition;
            direction.y = 0f;
            return true;
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

        private void TryFireHit()
        {
            if (!_configService.Dev.EnableNpcHitDetection)
                return;
            if (_move != FightAiMove.Attack && _move != FightAiMove.AttackSide && !_isRunAttackActive)
                return;

            var target = _enemy;
            if (target == null)
            {
                Logger.LogWarning($"[AttackPlayAni] {NpcInstance.GetName(NpcNameSlot.Slot0)} TryFireHit — enemy NpcContainer is null", LogCat.Fight);
                return;
            }

            if (Gothic.Core.Services.Npc.NpcHelperService.IsDown(target))
                return;

            // NPC vs NPC hit: check weapon reach + forward arc so the target can dodge by
            // stepping out of range or to the side. The +0.3m buffer accounts for body volume.
            // Real bone-collider detection (DEF_HIT_LIMB) should replace this once implemented.
            var attackerPos = NpcGo.transform.position;
            var targetPos = target.Go.transform.position;
            var toTarget = targetPos - attackerPos;
            toTarget.y = 0f;

            var reach = GetWeaponReach() + 0.3f;
            if (toTarget.magnitude > reach)
            {
                Logger.Log($"[AttackPlayAni] {NpcInstance.GetName(NpcNameSlot.Slot0)} miss — target out of reach ({toTarget.magnitude:F2}m > {reach:F2}m)", LogCat.Fight);
                return;
            }

            // 60° forward arc (30° each side) — matches Gothic's weapon swing.
            var angle = Vector3.Angle(NpcGo.transform.forward, toTarget.normalized);
            if (angle > 60f)
            {
                Logger.Log($"[AttackPlayAni] {NpcInstance.GetName(NpcNameSlot.Slot0)} miss — target outside arc ({angle:F0}°)", LogCat.Fight);
                return;
            }

            Logger.LogWarning($"[AttackPlayAni] HIT: {NpcInstance.GetName(NpcNameSlot.Slot0)} → {target.Instance.GetName(NpcNameSlot.Slot0)} dist={toTarget.magnitude:F2}m angle={angle:F0}°", LogCat.Fight);

            var weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;
            // DeveloperConfig.EnableSpellMonsterMelee: the web crawler bites its frozen/close target instead.
            if (weaponState == VmGothicEnums.WeaponState.Mage && !NpcContainer.IsSpellMonsterInMelee)
            {
                var mfxName = GetSpellMfxName();
                var spellDamage = GetNpcSpellDamage(mfxName);
                PlaySpellCastSound(mfxName);

                // Run Daedalus' Spell_ProcessMana so content-side effects (e.g. Spell_Logic_SummonSkeleton
                // -> Wld_SpawnNpcRange) fire for NPC casters exactly like they already do for the player in VRRuneCaster.
                var vm = GameStateService.GothicVm;
                var oldSelf = vm.GlobalSelf;
                var oldOther = vm.GlobalOther;
                vm.GlobalSelf = NpcInstance;
                vm.GlobalOther = target.Instance;
                try
                {
                    vm.Call<int, int>("Spell_ProcessMana", NpcContainer.ActiveSpellLevel);
                }
                finally
                {
                    vm.GlobalSelf = oldSelf;
                    vm.GlobalOther = oldOther;
                }

                // SpellHit both applies direct damage (if any) and fires PERC_ASSESSMAGIC on the
                // target (always the enemy here, since TryFireHit only runs for active fight moves),
                // so Daedalus content (ZS_MagicFreeze, ZS_MagicSleep, ZS_Zapped, Fear/Charm/Berzerk...)
                // drives the actual reaction — nothing per-spell to hardcode here.
                Logger.Log($"[AttackPlayAni] spell hit: ActiveSpell={NpcContainer.ActiveSpell} level={NpcContainer.ActiveSpellLevel} damage={spellDamage}", LogCat.Fight);
                // DeveloperConfig.EnableNpcSpellFx: a spell that flies hits when it arrives.
                var caster = NpcContainer;
                void Hit() => GlobalEventDispatcher.SpellHit.Invoke(caster, target,
                    target.Go != null ? target.Go.transform.position : targetPos, spellDamage);
                if (!_configService.Dev.EnableNpcSpellFx || !_particleService.PlayNpcSpell(mfxName, caster, target, Hit))
                {
                    Hit();
                    if (_configService.Dev.EnableNpcSpellFx && !SpellConst.AoeEffectNames.Contains(mfxName ?? ""))
                        _particleService.PlaySpellHitFx(mfxName, target);
                }
            }
            else
            {
                GlobalEventDispatcher.FightHit.Invoke(NpcContainer, target, attackerPos);
            }
        }

        private void PlaySpellCastSound(string mfxName)
        {
            if (string.IsNullOrEmpty(mfxName))
                return;
            var clip = _audioService.GetRandomSoundClip($"MFX_{mfxName}_Cast");
            if (clip == null)
                clip = _audioService.GetRandomSoundClip("MFX_Thunderbolt_Cast"); // fallback for spells with no dedicated cast SFX
            if (clip == null || PrefabProps.NpcSound == null)
                return;
            PrefabProps.NpcSound.PlayOneShot(clip);
        }

        private int GetNpcSpellDamage(string mfxName)
        {
            if (string.IsNullOrEmpty(mfxName))
                return 0;
            // DeveloperConfig.EnableOnlyBadSpellsDamage: like for the hero (VRRuneCaster), only SPELL_BAD spells hurt -
            // G2 Xardas' sleep inherits damage_per_level = 1 from C_Spell_Proto and hurt instead of putting to sleep.
            var spell = VmCacheService.TryGetSpellData(NpcContainer.ActiveSpell);
            var spellBad = GameStateService.GothicVm.GetSymbolByName("SPELL_BAD")?.GetInt(0) ?? 2;
            if (_configService.Dev.EnableOnlyBadSpellsDamage && spell != null && spell.SpellType != spellBad)
                return 0;
            var dmgSym = GameStateService.GothicVm.GetSymbolByName($"SPL_DAMAGE_{mfxName.ToUpper()}");
            // Fallback: the engine's SPELL_<effect>.damage_per_level (G2 fire storm: effect "Pyrokinesis").
            var baseDamage = dmgSym?.GetInt(0) ??
                             VmCacheService.TryGetSpellData(NpcContainer.ActiveSpell)?.DamagePerLevel ?? 0;
            return baseDamage * NpcContainer.ActiveSpellLevel;
        }

        private string GetSpellMfxName()
        {
            var mfxSym = GameStateService.GothicVm.GetSymbolByName("spellFXInstanceNames");
            return mfxSym?.GetString((ushort)NpcContainer.ActiveSpell);
        }

        private float GetWeaponReach()
        {
            var weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;
            // Matches Attack.cs's MyFkFocusFar routing: a ranged/magic attack can be dispatched from
            // anywhere within FK-range, not just the old artificial "close" W-range guess — otherwise
            // TryFireHit() rejects the very attacks the fight-AI just chose to fire as "out of reach".
            if (!NpcContainer.IsSpellMonsterInMelee &&
                weaponState is VmGothicEnums.WeaponState.Mage or VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow)
                return FightConst.RangedAttackReachMeters * _configService.Dev.RangedCombatRangeMultiplier;

            var baseRange = GameStateService.GuildValues.GetFightRangeBase(Vob.GuildTrue);
            // Fists / no weapon: CurrentItem is the last used item (a novice's joint, range 0) - fist range instead.
            // Reach was 0.3 m and every punch of a berzerk victim missed.
            var isMeleeWeaponDrawn = weaponState is VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H;
            var item = isMeleeWeaponDrawn ? VmCacheService.TryGetItemData(Props.CurrentItem) : null;
            var weaponRange = item?.Range ?? GameStateService.GuildValues.GetFightRangeFist(Vob.GuildTrue);
            // Same minimum as Attack.GetAttackRange(): the fight AI attacks from there, so the hit must connect there.
            return Mathf.Max((baseRange + weaponRange) / 100f, 1.5f);
        }
    }
}
