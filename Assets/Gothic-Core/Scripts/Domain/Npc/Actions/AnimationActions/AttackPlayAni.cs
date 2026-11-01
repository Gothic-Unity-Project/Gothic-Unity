using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// Basically PlayAni with special Attack handling: one fight move of AI_Attack (swing, run, strafe, ...).
    /// A swing hits like the engine at its DEF_OPT_FRAME (an animation without one at its end). A swing asked for in
    /// its DEF_WINDOW continues the same animation with its next part - a combo (AttackCombo).
    /// </summary>
    public class AttackPlayAni : PlayAni
    {
        // Running: control goes back to the state loop this often, so ZS_*_Attack_Loop can check the chase (it gives
        // up, $RUNCOWARD). The run animation keeps playing into the next step.
        private const float _runStepSeconds = 0.5f;
        private const float _runArrivalThreshold = 0.3f;
        private const float _turnThresholdDegrees = 10f;
        // A swing hits within the weapon's reach (+ the bodies' volume) and this angle to each side, so the target can
        // step back or aside. Gothic checks the weapon's bone (DEF_HIT_LIMB) - this is the same reach without it.
        private const float _hitArcDegrees = 60f;
        // The VR hero is hit at his chest, this far below his head (the blood comes from there).
        private const float _heroChestBelowHead = 0.4f;

        // Ranged and magic fights of NPCs aren't there yet - their attacks don't hit. Logged once per NPC.
        private static readonly HashSet<int> _rangedAttackLogged = new();

        [Inject] private readonly FightService _fightService;

        private FightAiMove _move => (FightAiMove)Action.Int0;
        private NpcContainer _enemy => Action.Instance0.GetUserData();
        private AttackCombo _combo => Props.AttackCombo;

        private bool _hasHitFired;
        private bool _isNextAttackRequested;
        // Which part of a combo animation this swing is (its hit frame, window and end).
        private int _comboPart;
        private float _runStepTime;
        private string _turnLeftAnimName;
        private string _turnRightAnimName;
        private string _activeTurnAnimName;

        /// <summary>
        /// The swing hit and its combo window is open: an attack queued now continues the combo. Nothing queued yet -
        /// AiHandler asks the state's loop for the next move once (the engine's fight AI runs on all the time).
        /// </summary>
        public bool IsWaitingForNextAttack
        {
            get
            {
                if (IsFinishedFlag || !_hasHitFired || _isNextAttackRequested || !IsMeleeAttackMove() ||
                    !TryGetAttackTime(out var time, out var fps, out _))
                    return false;

                var frames = PrefabProps.AnimationSystem.AttackFrames;
                return _comboPart + 1 < AttackCombo.GetPartCount(frames) &&
                       AttackCombo.IsWindowOpen(frames, _comboPart, time, fps);
            }
        }

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
            if (enemy?.Go == null || enemy.IsDown())
            {
                IsFinishedFlag = true;
                return;
            }

            _turnLeftAnimName = AnimationService.GetAnimationName(VmGothicEnums.AnimationType.RotL, NpcContainer);
            _turnRightAnimName = AnimationService.GetAnimationName(VmGothicEnums.AnimationType.RotR, NpcContainer);

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
            var weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;
            return weaponState.IsRanged() || weaponState == VmGothicEnums.WeaponState.Mage;
        }

        private bool TryGetAttackTime(out float time, out float fps, out float duration)
        {
            return PrefabProps.AnimationSystem.TryGetAttackTrackTime(Action.String0, out time, out fps, out duration);
        }

        /// <summary>
        /// The previous swing left its animation playing for this one: the same animation continues at the end of its
        /// last part instead of starting over.
        /// </summary>
        private bool TryContinueCombo()
        {
            var animationName = _combo.ContinueAnimation;
            var part = _combo.ContinuePart;
            _combo.ContinueAnimation = null;
            if (animationName == null || !IsMeleeAttackMove())
                return false;

            if (!animationName.EqualsIgnoreCase(Action.String0))
            {
                // Another attack animation (left/right): it starts fresh, the old one goes.
                PrefabProps.AnimationSystem.StopAnimation(animationName);
                return false;
            }

            var frames = PrefabProps.AnimationSystem.AttackFrames;
            if (!TryGetAttackTime(out _, out var fps, out var duration) ||
                !AttackCombo.TryGetContinueTime(frames, part, fps, out var continueTime))
                return false;

            PrefabProps.AnimationSystem.SetAttackTrackTime(Action.String0, continueTime);
            TryGetAttackTime(out var time, out _, out _);
            _comboPart = part;
            ActionEndEventTime = Mathf.Max(duration - time, 0.01f);
            return true;
        }

        public override void Tick()
        {
            if (_enemy?.Go == null)
            {
                StopTurnAnimation();
                IsFinishedFlag = true;
                return;
            }

            base.Tick();

            if (IsFinishedFlag)
            {
                // The attack played to its end - the next one starts a new combo.
                _combo.Length = 0;
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
        /// This part hits at its DEF_OPT_FRAME. An attack queued in its DEF_WINDOW continues the same animation if it
        /// has another part (hand-over to the next action), without one the attack ends at the part's DEF_HIT_END.
        /// At most as many follow-ups as windows: ZS_MM_Attack_Loop queues AI_Attack every loop, a lurker would
        /// chain bites forever.
        /// </summary>
        private void TickCombo()
        {
            if (!TryGetAttackTime(out var time, out var fps, out _))
                return;

            var frames = PrefabProps.AnimationSystem.AttackFrames;
            if (!_hasHitFired && AttackCombo.IsHitFrameReached(frames, _comboPart, time, fps))
            {
                _hasHitFired = true;
                TryFireHit();
            }

            if (!_hasHitFired)
                return;

            var hasNextPart = _comboPart + 1 < AttackCombo.GetPartCount(frames);
            var isComboLeft = _combo.Length < AttackCombo.GetWindowCount(frames);
            if (hasNextPart && isComboLeft && Props.AnimationQueue.Count > 0 &&
                AttackCombo.IsWindowOpen(frames, _comboPart, time, fps))
            {
                _combo.Length++;
                _combo.ContinueAnimation = Action.String0;
                _combo.ContinuePart = _comboPart + 1;
                IsFinishedFlag = true;
                return;
            }

            if (AttackCombo.IsHitEndReached(frames, _comboPart, time, fps))
            {
                _combo.Length = 0;
                PrefabProps.AnimationSystem.StopAnimation(Action.String0);
                IsFinishedFlag = true;
            }
        }

        /// <summary>
        /// Swings and fight moves keep facing the enemy, with the turn animations.
        /// </summary>
        private void HandleCombatRotation()
        {
            var direction = _enemy.GetFightPosition() - NpcGo.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
                return;

            var angle = Vector3.SignedAngle(NpcGo.transform.forward, direction.normalized, Vector3.up);
            if (Mathf.Abs(angle) <= _turnThresholdDegrees)
            {
                StopTurnAnimation();
                return;
            }

            var desiredAnim = angle < 0 ? _turnLeftAnimName : _turnRightAnimName;
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
            var targetPosition = _enemy.GetFightPosition();
            var toTarget = new Vector3(targetPosition.x - myPosition.x, 0f, targetPosition.z - myPosition.z);

            // The run ends where the next AI_Attack's weapon range starts - measured like AI_Attack measures it.
            var tooFarBy = _fightService.GetFightDistance(NpcContainer, _enemy) -
                           _fightService.GetAttackReach(NpcContainer);
            if (tooFarBy <= 0f)
            {
                StopRunning();
                return;
            }

            // Each attacker runs to the point on its own side of the enemy, so several spread around it instead of
            // stacking up.
            var approachDistance = Mathf.Max(0f, toTarget.magnitude - tooFarBy - _runArrivalThreshold);
            var approachPoint = targetPosition - toTarget.normalized * approachDistance;
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
            var direction = _enemy.GetFightPosition() - NpcGo.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            // e.g., Goblins aren't rotating fast enough to rotate around the target. Therefore *2;
            NpcGo.transform.rotation = Quaternion.RotateTowards(NpcGo.transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up), Time.deltaTime * GetTurnSpeed() * 2);
        }

        private float GetTurnSpeed()
        {
            return GameStateService.GuildValues.GetTurnSpeed(NpcInstance.GetValuesGuild());
        }

        private void TryFireHit()
        {
            var target = _enemy;
            if (target?.Go == null || target.IsDown())
                return;

            if (IsRangedFightMode())
            {
                if (_rangedAttackLogged.Add(NpcInstance.Index))
                    Logger.LogWarning($"[AttackPlayAni] {NpcInstance.GetName(NpcNameSlot.Slot0)} attacks with " +
                                      $"{(VmGothicEnums.WeaponState)Vob.FightMode} - ranged and magic hits of NPCs " +
                                      "aren't implemented yet.", LogCat.Fight);
                return;
            }

            if (_fightService.GetFightDistance(NpcContainer, target) > _fightService.GetMeleeReach(NpcContainer))
                return;

            var toTarget = target.GetFightPosition() - NpcGo.transform.position;
            toTarget.y = 0f;

            if (Vector3.Angle(NpcGo.transform.forward, toTarget.normalized) > _hitArcDegrees)
                return;

            // NPCs hit with their equipped weapon - FightService takes it from there.
            GlobalEventDispatcher.FightHit.Invoke(NpcContainer, target, GetHitPoint(target), null);
        }

        /// <summary>
        /// Where the hit lands on the target: the hero's chest, an NPC's body (BIP01), else its root.
        /// </summary>
        private static Vector3 GetHitPoint(NpcContainer target)
        {
            if (target.IsHero() && target.PrefabProps.Head != null)
                return target.PrefabProps.Head.position + Vector3.down * _heroChestBelowHead;

            var body = target.PrefabProps?.Bip01;
            return body != null ? body.position : target.Go.transform.position;
        }
    }
}
