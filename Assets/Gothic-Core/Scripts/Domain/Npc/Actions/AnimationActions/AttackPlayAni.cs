using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Npc;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// Basically PlayAni with special Attack handling: one fight move of AI_Attack (swing, run, strafe, ...).
    /// A swing hits like the engine at its DEF_OPT_FRAME (an animation without one at its end). A swing asked for in
    /// its DEF_WINDOW continues the same animation with its next part - a combo (OpenGothic Pose::continueCombo).
    /// </summary>
    public class AttackPlayAni : PlayAni
    {
        // Running: control goes back to the state loop this often, so ZS_*_Attack_Loop can check the chase (it gives up,
        // $RUNCOWARD). The run animation keeps playing into the next step.
        private const float _runStepSeconds = 0.5f;
        // Attack.GetDistance() takes this off the distance (the bodies' volume): the run stops this much further out.
        private const float _bodyVolumeOffset = 0.3f;
        private const float _minRunStopDistance = 1.11f;
        private const float _runArrivalThreshold = 0.3f;
        private const float _turnThresholdDegrees = 10f;
        // A swing hits within the weapon's reach (+ the bodies' volume) and this angle to each side, so the target can
        // step back or aside. Gothic checks the weapon's bone (DEF_HIT_LIMB) - this is the same reach without it.
        private const float _hitArcDegrees = 60f;
        // Same minimum as Attack.GetAttackRange(): the fight AI attacks from there, so the hit must connect there.
        private const float _minWeaponReach = 1.5f;

        // Ranged and magic fights of NPCs aren't there yet - their attacks don't hit. Logged once per NPC.
        private static readonly HashSet<int> _rangedAttackLogged = new();

        private FightAiMove _move => (FightAiMove)Action.Int0;
        private NpcContainer _enemy => Action.Instance0.GetUserData();
        private Transform _enemyTransform => _enemy.Go.transform;

        private bool _hasHitFired;
        private bool _isNextAttackRequested;
        // Which part of a combo animation this swing is (its hit frame, window and end).
        private int _comboIndex;
        private float _runStepTime;
        private string _activeTurnAnimName;

        /// <summary>
        /// The swing hit and its combo window is open: an attack queued now continues the combo. Nothing queued yet -
        /// AiHandler asks the state's loop for the next move once (the engine's fight AI runs on all the time).
        /// </summary>
        public bool IsWaitingForNextAttack =>
            !IsFinishedFlag && _hasHitFired && !_isNextAttackRequested && IsMeleeAttackMove() &&
            _comboIndex + 1 < PrefabProps.AnimationSystem.OptimalFrameCount &&
            PrefabProps.AnimationSystem.IsComboWindowOpen(Action.String0, _comboIndex);

        public AttackPlayAni(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public void MarkNextAttackRequested()
        {
            _isNextAttackRequested = true;
        }

        public override void Start()
        {
            var enemy = _enemy;
            if (enemy?.Go == null || NpcHelperService.IsDown(enemy))
            {
                IsFinishedFlag = true;
                return;
            }

            if (TryContinueCombo())
                return;

            base.Start();
        }

        private bool IsMeleeAttackMove()
        {
            return _move is FightAiMove.Attack or FightAiMove.AttackSide && !IsRangedFightMode();
        }

        private bool IsRangedFightMode()
        {
            return (VmGothicEnums.WeaponState)Vob.FightMode is VmGothicEnums.WeaponState.Mage or
                VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow;
        }

        /// <summary>
        /// The previous swing left its animation playing for this one: the same animation continues at the end of its
        /// last part (OpenGothic Pose::continueCombo) instead of starting over.
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
            return true;
        }

        public override void Tick()
        {
            base.Tick();

            if (IsFinishedFlag)
            {
                // The attack played to its end - the next one starts a new combo.
                Props.AttackComboLength = 0;
                StopTurnAnimation();

                // A swing without DEF_OPT_FRAME (wolves' bites) hits at its end.
                if (!_hasHitFired && IsMeleeAttackMove())
                {
                    _hasHitFired = true;
                    TryFireHit();
                }
                return;
            }

            if (IsMeleeAttackMove())
            {
                TickCombo();
                if (IsFinishedFlag)
                    return;
            }

            switch (_move)
            {
                case FightAiMove.Run:
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
        /// Like the engine: this part hits at its DEF_OPT_FRAME. An attack queued in its DEF_WINDOW continues the same
        /// animation if it has another part (hand-over to the next action), without one the attack ends at the part's
        /// DEF_HIT_END. A combo has at most as many follow-ups as the animation has windows: ZS_MM_Attack_Loop queues
        /// AI_Attack every loop, a lurker would chain bites forever.
        /// </summary>
        private void TickCombo()
        {
            var animationSystem = PrefabProps.AnimationSystem;
            if (!_hasHitFired && animationSystem.HasOptimalFrameReached(Action.String0, _comboIndex))
            {
                _hasHitFired = true;
                TryFireHit();
            }

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

        /// <summary>
        /// Swings and fight moves keep facing the enemy, with the turn animations.
        /// </summary>
        private void HandleCombatRotation()
        {
            var direction = _enemyTransform.position - NpcGo.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
                return;

            var angle = Vector3.SignedAngle(NpcGo.transform.forward, direction.normalized, Vector3.up);
            if (Mathf.Abs(angle) <= _turnThresholdDegrees)
            {
                StopTurnAnimation();
                return;
            }

            var desiredAnim = AnimationService.GetAnimationName(
                angle < 0 ? VmGothicEnums.AnimationType.RotL : VmGothicEnums.AnimationType.RotR, NpcContainer);
            if (_activeTurnAnimName != desiredAnim)
            {
                StopTurnAnimation();
                _activeTurnAnimName = desiredAnim;
                PrefabProps.AnimationSystem.PlayAnimation(_activeTurnAnimName);
            }

            NpcGo.transform.rotation = Quaternion.RotateTowards(NpcGo.transform.rotation,
                Quaternion.LookRotation(direction), Time.deltaTime * GetTurnSpeed());
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
            // Like the engine's AI_Attack (one step per loop): control goes back to the state loop now and then, so
            // ZS_*_Attack_Loop sees a fleeing target during the chase - before, it only ran again on arrival.
            _runStepTime += Time.deltaTime;
            if (_runStepTime >= _runStepSeconds && Props.AnimationQueue.Count == 0)
            {
                IsFinishedFlag = true;
                return;
            }

            var myPosition = NpcGo.transform.position;
            var targetPosition = _enemyTransform.position;
            var toTarget = new Vector3(targetPosition.x - myPosition.x, 0f, targetPosition.z - myPosition.z);

            // The run ends where the next AI_Attack's weapon range starts.
            var stopDistance = Mathf.Max(GetWeaponReach() + _bodyVolumeOffset, _minRunStopDistance);
            if (toTarget.magnitude <= stopDistance)
            {
                StopRunning();
                return;
            }

            // Each attacker runs to the point on its own side of the enemy, so several spread around it instead of
            // stacking up.
            var approachPoint = targetPosition - toTarget.normalized * Mathf.Max(0f, stopDistance - _runArrivalThreshold);
            var toApproachPoint = new Vector3(approachPoint.x - myPosition.x, 0f, approachPoint.z - myPosition.z);
            if (toApproachPoint.magnitude <= _runArrivalThreshold)
            {
                StopRunning();
                return;
            }

            NpcGo.transform.rotation = Quaternion.RotateTowards(NpcGo.transform.rotation,
                Quaternion.LookRotation(toTarget), Time.deltaTime * GetTurnSpeed());
        }

        private void StopRunning()
        {
            PrefabProps.AnimationSystem.StopAllAnimations();
            IsFinishedFlag = true;
        }

        private void StrafeTick()
        {
            // e.g., Goblins aren't rotating fast enough to rotate around the target. Therefore *2;
            var direction = _enemyTransform.position - NpcGo.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            NpcGo.transform.rotation = Quaternion.RotateTowards(NpcGo.transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up), Time.deltaTime * GetTurnSpeed() * 2);
        }

        /// <summary>
        /// Rotation speed: the human guild value for any type of human, else the monster guild itself.
        /// </summary>
        private float GetTurnSpeed()
        {
            var guild = NpcInstance.Guild <= (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM
                ? (int)VmGothicEnums.Guild.GIL_HUMAN
                : NpcInstance.Guild;
            return GameStateService.GuildValues.GetTurnSpeed(guild);
        }

        private void TryFireHit()
        {
            var target = _enemy;
            if (target?.Go == null || NpcHelperService.IsDown(target))
                return;

            if (IsRangedFightMode())
            {
                if (_rangedAttackLogged.Add(NpcInstance.Index))
                    Logger.LogWarning($"[AttackPlayAni] {NpcInstance.GetName(NpcNameSlot.Slot0)} attacks with " +
                                      $"{(VmGothicEnums.WeaponState)Vob.FightMode} - ranged and magic hits of NPCs " +
                                      "aren't implemented yet.", LogCat.Fight);
                return;
            }

            var attackerPosition = NpcGo.transform.position;
            var toTarget = target.Go.transform.position - attackerPosition;
            toTarget.y = 0f;

            var reach = GetWeaponReach() + _bodyVolumeOffset;
            if (toTarget.magnitude > reach)
                return;

            if (Vector3.Angle(NpcGo.transform.forward, toTarget.normalized) > _hitArcDegrees)
                return;

            GlobalEventDispatcher.FightHit.Invoke(NpcContainer, target, attackerPosition);
        }

        private float GetWeaponReach()
        {
            var baseRange = GameStateService.GuildValues.GetFightRangeBase(Vob.GuildTrue);
            // Fists / no weapon: CurrentItem can be the last used item (a novice's joint, range 0) - fist range then.
            var isMeleeWeaponDrawn = (VmGothicEnums.WeaponState)Vob.FightMode is VmGothicEnums.WeaponState.W1H or
                VmGothicEnums.WeaponState.W2H;
            var item = isMeleeWeaponDrawn ? VmCacheService.TryGetItemData(Props.CurrentItem) : null;
            var weaponRange = item?.Range ?? GameStateService.GuildValues.GetFightRangeFist(Vob.GuildTrue);
            return Mathf.Max((baseRange + weaponRange) / 100f, _minWeaponReach);
        }
    }
}
