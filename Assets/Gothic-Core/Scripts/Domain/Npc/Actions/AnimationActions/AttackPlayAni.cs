using Gothic.Core.Models.Npc;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Logger = Gothic.Core.Logging.Logger;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
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
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly ProjectileService _projectileService;
        [Inject] private readonly INpcJumpFallFactory _npcJumpFallFactory;

        private FightAiMove _move => (FightAiMove)Action.Int0;
        public FightAiMove Move => _move;
        private NpcContainer _enemy => Action.Instance0.GetUserData();
        private Transform _enemyTransform => _enemy.Go.transform;
        private bool _comboWindowLogged;
        private bool _hasHitFired;
        // Which part of a combo animation this attack is (its hit frame, window, end).
        private int _comboIndex;
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

        // Steering, climbing and the give-up timer of the run after the enemy.
        private readonly NpcChaseMovement _chase;

        // Swimming: no attacks, only swimming after the target.
        private const string _swimIdleAnimName = "S_SWIM";
        private const string _swimForwardAnimName = "S_SWIMF";
        private const float _swimWaitSeconds = 0.5f;
        private string _swimAnimName;

        private const float _runStepSeconds = 0.5f;
        // How often a blocked archer's strafe/run checks the shot's path,
        // and how close his run goes when it never clears (the script switches to melee there).
        private const float _shotPathCheckSeconds = 0.15f;
        private const float _blockedShotRunStopDistance = 2f;
        private float _shotPathCheckTime;
        private float _runStepTime;


        public AttackPlayAni(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
            _chase = new NpcChaseMovement(npcContainer).Inject();
        }

        public override void Start()
        {
            if (Gothic.Core.Services.Npc.NpcHelperService.IsDown(_enemy))
            {
                IsFinishedFlag = true;
                return;
            }

            // Falling/climbing (NpcJumpFall): no fight move in the air - the next AI_Attack loop continues afterwards.
            if (_npcJumpFallFactory.Get(NpcContainer)?.IsBusy == true)
            {
                IsFinishedFlag = true;
                return;
            }

            if (IsSwimming())
            {
                StartSwimming();
                return;
            }

            if (TryContinueCombo())
                return;
            base.Start();

            // This run either ends in a strike out of the run or, like before,
            // stops in range and the next AI_Attack strikes from a stand.
            _isRunAttackPlanned = _move == FightAiMove.Run && Random.value < _runAttackChance;
        }

        private bool IsMeleeAttackMove()
        {
            return _move is FightAiMove.Attack or FightAiMove.AttackSide && _swimAnimName == null &&
                   (VmGothicEnums.WeaponState)Vob.FightMode is not (VmGothicEnums.WeaponState.Mage or
                       VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow);
        }

        /// <summary>
        /// The previous attack left its animation playing for us - the same
        /// animation continues at the end of its last part (OpenGothic Pose::continueCombo) instead of starting over.
        /// </summary>
        private bool TryContinueCombo()
        {
            var animationName = Props.ComboContinueAnimation;
            var index = Props.ComboContinueIndex;
            Props.ComboContinueAnimation = null;
            if (animationName == null || !IsMeleeAttackMove())
                return false;

            if (!animationName.EqualsIgnoreCase(Action.String0))
            {
                // Another attack animation (left/right): it starts fresh, the old one goes.
                PrefabProps.AnimationSystem.StopAnimation(animationName);
                return false;
            }

            var secondsLeft = PrefabProps.AnimationSystem.ContinueCombo(Action.String0, index);
            if (secondsLeft < 0f)
                return false;

            _comboIndex = index;
            ActionEndEventTime = Mathf.Max(secondsLeft, 0.01f);
            Logger.Log($"[Combo] {NpcInstance.GetName(NpcNameSlot.Slot0)} continues {Action.String0} with part " +
                       $"{index + 1}/{PrefabProps.AnimationSystem.OptimalFrameCount}", LogCat.Animation);
            return true;
        }

        /// <summary>
        /// Like the engine: this part hits at its DEF_OPT_FRAME; an attack queued
        /// in its DEF_WINDOW continues the same animation if it has another part (hand-over to the next action), else the
        /// next attack waits; without one the attack ends at the part's DEF_HIT_END. Before, the whole animation played:
        /// a master's later swings did no damage, and a restarted single-hit attack (shadowbeast) hit twice per swing.
        /// </summary>
        private void TickEngineCombo()
        {
            var animationSystem = PrefabProps.AnimationSystem;
            if (!_hasHitFired && animationSystem.HasOptimalFrameReached(Action.String0, _comboIndex))
                FireHitOnce();

            if (!_hasHitFired)
                return;

            var hasNextPart = _comboIndex + 1 < animationSystem.OptimalFrameCount;
            var isComboLeft = Props.AttackComboLength < animationSystem.ComboWindowCount;
            if (hasNextPart && isComboLeft && Props.AnimationQueue.Count > 0 &&
                animationSystem.IsComboWindowOpen(Action.String0, _comboIndex))
            {
                Props.AttackComboLength++;
                Props.ComboContinueAnimation = Action.String0;
                Props.ComboContinueIndex = _comboIndex + 1;
                Logger.Log($"[Combo] {NpcInstance.GetName(NpcNameSlot.Slot0)} part {_comboIndex + 1} -> next part " +
                           $"(queue={Props.AnimationQueue.Count})", LogCat.Animation);
                IsFinishedFlag = true;
                return;
            }

            if (animationSystem.HasHitEndReached(Action.String0, _comboIndex))
            {
                Props.AttackComboLength = 0;
                animationSystem.StopAnimation(Action.String0);
                IsFinishedFlag = true;
            }
        }

        private bool IsSwimming()
        {
            return Vob.AiHuman?.WaterLevel == (int)ZenGineConst.WaterLevel.Chest;
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
                OnAttackAnimationFinished();
                return;
            }

            if (IsMeleeAttackMove())
            {
                TickEngineCombo();
                if (IsFinishedFlag)
                    return;
            }
            else
            {
                // Like the engine, the hit lands at DEF_OPT_FRAME.
                if (!_hasHitFired && PrefabProps.AnimationSystem.HasOptimalFrameReached(Action.String0))
                    FireHitOnce();
            }

            if (!IsMeleeAttackMove() && PrefabProps.AnimationSystem.HasComboWindowOpened && TickComboWindow())
                return;

            TickMove();
        }

        /// <summary>
        /// The attack played to its end - the next one starts a new combo.
        /// </summary>
        private void OnAttackAnimationFinished()
        {
            Props.AttackComboLength = 0;
            StopTurnAnimation();
            // Magic/ranged animations have no DEF_OPT_FRAME, so HasComboWindowOpened never fires.
            // Fire the hit at animation end instead.
            // A melee animation without DEF_OPT_FRAME/DEF_WINDOW hits
            // at its end too - wolves' bites never checked for a hit.
            if (!_hasHitFired)
                FireHitOnce();

            if (!_isRangedFollowUp)
                TryStartRangedFollowUp();
        }

        /// <summary>
        /// Combo chaining for non-engine-combo attacks: once the DEF_WINDOW frame is reached, cut this animation short
        /// so the next queued attack starts immediately — exactly like Gothic's original combo system.
        /// The last attack in a sequence has nothing queued, so it plays to full completion.
        /// True when the attack ended here.
        /// </summary>
        private bool TickComboWindow()
        {
            // Auto-register hit at the attack frame (replaces bone collider detection, temporary debug aid).
            if (!_hasHitFired)
                FireHitOnce();

            // Like the engine (OpenGothic Pose::continueCombo), a combo has as
            // many follow-up hits as the animation has DEF_WINDOW pairs - then it breaks and the attack plays to its
            // end. ZS_MM_Attack_Loop queues AI_Attack every loop: a lurker chained 4 bites a second forever.
            var isComboLeft = Props.AttackComboLength < PrefabProps.AnimationSystem.ComboWindowCount;
            // Like the engine (OpenGothic Pose::continueCombo) a combo
            // continues the same animation to its next hit frame - one with a single DEF_OPT_FRAME (shadowbeast,
            // wolf) has no second hit. Restarted here, it hit again 0.3 s later: one swing, the hero's HP twice.
            if (PrefabProps.AnimationSystem.OptimalFrameCount <= Props.AttackComboLength + 1)
                isComboLeft = false;
            if (Props.AnimationQueue.Count > 0 && isComboLeft)
            {
                Props.AttackComboLength++;
                Logger.LogWarning($"[Combo] {NpcInstance.GetName(NpcNameSlot.Slot0)} cutting anim early → next hit " +
                                  $"(queue={Props.AnimationQueue.Count}, combo {Props.AttackComboLength}/" +
                                  $"{PrefabProps.AnimationSystem.ComboWindowCount})", LogCat.Animation);
                // Stop the current track so IsAlreadyPlaying won't block the next attack from starting fresh.
                PrefabProps.AnimationSystem.StopAnimation(Action.String0);
                IsFinishedFlag = true;
                return true;
            }

            // Enemy died during this attack (loop returned LOOP_END) — cut at combo window
            // instead of letting the full animation play out.
            if (Props.CurrentLoopState == LoopState.End)
            {
                Logger.LogWarning($"[Combo] {NpcInstance.GetName(NpcNameSlot.Slot0)} enemy down — cutting at combo " +
                                  "window", LogCat.Animation);
                PrefabProps.AnimationSystem.StopAnimation(Action.String0);
                IsFinishedFlag = true;
                return true;
            }

            if (!_comboWindowLogged)
            {
                _comboWindowLogged = true;
                Logger.Log($"[Combo] {NpcInstance.GetName(NpcNameSlot.Slot0)} window open, queue empty - last anim " +
                           "plays to end", LogCat.Animation);
            }
            return false;
        }

        private void TickMove()
        {
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

        /// <summary>
        /// The hit of this attack, at most once.
        /// </summary>
        private void FireHitOnce()
        {
            _hasHitFired = true;
            TryFireHit();
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

            var guild = NpcInstance.GetValuesGuild();
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
            var guild = NpcInstance.GetValuesGuild();
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
            if (IsRunStepOver())
            {
                IsFinishedFlag = true;
                return;
            }

            var myPosition = NpcContainer.Go.transform.position;
            var targetPosition = _enemyTransform.position;

            var myPositionH = new Vector3(myPosition.x, 0, myPosition.z);
            var targetPositionH = new Vector3(targetPosition.x, 0, targetPosition.z);
            var toTarget = targetPositionH - myPositionH;

            // A blocked archer runs closer until he has a free shot -
            // his bow's W-range (G1 21 m) stopped the run after one frame, every frame: he shook forward in place.
            if (IsBlockedShotMove() && HasShotPathCleared())
            {
                Logger.Log($"[Attack] {NpcInstance.GetName(NpcNameSlot.Slot0)} has a free line of shot - stops " +
                           "running", LogCat.Fight);
                StopRunning();
                return;
            }
            var stopDistance = GetRunStopDistance();

            // The strike out of the run starts a bit before the stop distance - the move attack lunges forward.
            if (_isRunAttackPlanned && toTarget.magnitude <= stopDistance + _runAttackLeadDistance &&
                TryStartRunAttack())
                return;

            if (toTarget.magnitude <= stopDistance)
            {
                StopRunning();
                return;
            }

            switch (_chase.Steer(myPosition, targetPosition, toTarget))
            {
                case NpcChaseMovement.Step.JumpFallStarted:
                    IsFinishedFlag = true;
                    return;
                case NpcChaseMovement.Step.Unreachable:
                    StopRunning();
                    return;
            }

            if (_chase.HasHeroRunAway())
                GiveUpChase();
        }

        /// <summary>
        /// Like the engine's AI_Attack (one step per loop): hand control back to the state loop now and then while
        /// running, so ZS_*_Attack_Loop can check "the target ran > 10 s" during the chase (it gives up, says
        /// $RUNCOWARD, T_WARN in _End). Before, the loop only ran again on arrival - orcs chased all the way and
        /// then put their weapon away right next to the hero. The run animation keeps playing into the next step.
        /// </summary>
        private bool IsRunStepOver()
        {
            _runStepTime += Time.deltaTime;
            return _runStepTime >= _runStepSeconds && Props.AnimationQueue.Count == 0;
        }

        /// <summary>
        /// Like the engine (GoTo::isClose for GT_Enemy = FightAlgo::isInWRange, OpenGothic npc.cpp:52-54), the run
        /// stops in W-range: the preferred attack distance, the same the fight table choice in Attack.cs uses. A
        /// blocked archer (IsBlockedShotMove) runs up to _blockedShotRunStopDistance instead.
        /// </summary>
        private float GetRunStopDistance()
        {
            if (IsBlockedShotMove())
                return _blockedShotRunStopDistance;
            return GetWeaponReach();
        }

        private void GiveUpChase()
        {
            Logger.LogWarning($"[AttackPlayAni] {NpcInstance.GetName(NpcNameSlot.Slot0)}: hero ran away — giving up " +
                              $"after {_chase.ChaseSeconds:F1}s", LogCat.Fight);
            PrefabProps.AnimationSystem.StopAllAnimations();
            Props.AnimationQueue.Clear();
            Props.CurrentLoopState = LoopState.End;
            IsFinishedFlag = true;
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

            FireHitOnce();
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
        /// The NPC's own swing before its hit frame ends when the hero swings (FightService.OnHeroMeleeSwing) - the
        /// next AI_Attack can parry or jump back instead.
        /// </summary>
        public bool TryInterruptSwing()
        {
            if (!IsSwingInProgress)
                return false;

            PrefabProps.AnimationSystem.StopAnimation(Action.String0);
            IsFinishedFlag = true;
            return true;
        }

        /// <summary>
        /// A strafe ends when the hero swings - the next AI_Attack reacts.
        /// </summary>
        public bool TryInterruptStrafe()
        {
            if (IsFinishedFlag || _move != FightAiMove.Strafe)
                return false;

            PrefabProps.AnimationSystem.StopAnimation(Action.String0);
            IsFinishedFlag = true;
            return true;
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
        /// This strafe/run is an archer's way around cover
        /// (Attack.AvoidBlockedShot).
        /// </summary>
        private bool IsBlockedShotMove()
        {
            return Props.BlockedShotCount > 0 &&
                   (VmGothicEnums.WeaponState)Vob.FightMode is VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow;
        }

        /// <summary>
        /// The shot's path to the enemy is free again (checked every _shotPathCheckSeconds).
        /// </summary>
        private bool HasShotPathCleared()
        {
            _shotPathCheckTime += Time.deltaTime;
            if (_shotPathCheckTime < _shotPathCheckSeconds)
                return false;
            _shotPathCheckTime = 0f;
            return _projectileService.HasLineOfShot(NpcContainer, _enemy);
        }

        private void StrafeTick()
        {
            // The step aside ends as soon as the shot is free - the full
            // step carried Lukis past the gap between a pillar and a tree, behind the tree.
            if (IsBlockedShotMove() && HasShotPathCleared())
            {
                Logger.Log($"[Attack] {NpcInstance.GetName(NpcNameSlot.Slot0)} has a free line of shot - stops " +
                           "stepping aside", LogCat.Fight);
                TryInterruptStrafe();
                return;
            }

            // For rotation speed, we use the guild value for human if any type of human or the monster guild itself.
            var guild = NpcInstance.GetValuesGuild();

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

            // Like the engine, a bow/crossbow shot is a flying arrow/bolt
            // (ProjectileService) that hits whatever it meets - the target can dodge or take cover. Without ammo nothing
            // flies; B_SelectWeapon switches to the melee weapon (Npc_HasRangedWeaponWithAmmo).
            var weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;
            if (weaponState is VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow)
            {
                _projectileService.ShootAtTarget(NpcContainer, target);
                return;
            }

            // NPC vs NPC hit: check weapon reach + forward arc so the target can dodge by
            // stepping out of range or to the side. The reach is the engine's (Npc::commitDamage: isInAttackRange,
            // OpenGothic npc.cpp:2010), the same distance the run stops at.
            // Real bone-collider detection (DEF_HIT_LIMB) should replace this once implemented.
            var attackerPos = NpcGo.transform.position;
            var targetPos = target.Go.transform.position;
            var toTarget = targetPos - attackerPos;
            toTarget.y = 0f;

            var reach = GetWeaponReach();
            if (toTarget.magnitude > reach)
            {
                Logger.LogEditor($"[AttackPlayAni] {NpcInstance.GetName(NpcNameSlot.Slot0)} miss — target out of " +
                    $"reach ({toTarget.magnitude:F2}m > {reach:F2}m)", LogCat.Fight);
                return;
            }

            // 60° forward arc (30° each side) — matches Gothic's weapon swing.
            var angle = Vector3.Angle(NpcGo.transform.forward, toTarget.normalized);
            if (angle > 60f)
            {
                Logger.LogEditor($"[AttackPlayAni] {NpcInstance.GetName(NpcNameSlot.Slot0)} miss — target outside " +
                    $"arc ({angle:F0}°)", LogCat.Fight);
                return;
            }

            Logger.LogEditor($"[AttackPlayAni] HIT: {NpcInstance.GetName(NpcNameSlot.Slot0)} → " +
                $"{target.Instance.GetName(NpcNameSlot.Slot0)} dist={toTarget.magnitude:F2}m angle={angle:F0}°",
                LogCat.Fight);

            // The web crawler bites its frozen/close target instead.
            if (weaponState == VmGothicEnums.WeaponState.Mage && !NpcContainer.IsSpellMonsterInMelee)
                _fightService.CastNpcSpell(NpcContainer, target, targetPos);
            else
                GlobalEventDispatcher.FightHit.Invoke(NpcContainer, target, attackerPos);
        }

        /// <summary>
        /// The engine's preferred attack distance against the enemy (FightRangeDomain): guild bases + weapon range,
        /// for bows the reference bow range, for spells MaxMagRange. A spell monster biting uses its fist range.
        /// </summary>
        private float GetWeaponReach()
        {
            return _fightService.GetPreferredAttackDistance(NpcContainer, _enemy, NpcContainer.IsSpellMonsterInMelee);
        }
    }
}
