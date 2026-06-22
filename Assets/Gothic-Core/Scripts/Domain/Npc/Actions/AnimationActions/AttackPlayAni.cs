using Gothic.Core.Adapters.Properties;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Logger = Gothic.Core.Logging.Logger;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Config;
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

        private FightAiMove _move => (FightAiMove)Action.Int0;
        private NpcContainer _enemy => Action.Instance0.GetUserData();
        private Transform _enemyTransform => _enemy.Go.transform;
        private bool _comboWindowLogged;
        private bool _hasHitFired;
        private string _activeTurnAnimName;
        private const float _turnThresholdDeg = 10f;

        private float _chaseTimer;
        private float _heroStopTimer;
        private Vector3 _previousHeroPos;
        private bool _firstRunTick = true;
        private const float _chaseGiveUpDuration = 10f;
        private const float _heroStopResetDelay = 2f;
        private const float _heroRunSpeedThreshold = 2f;


        public AttackPlayAni(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Tick()
        {
            base.Tick();

            if (IsFinishedFlag)
            {
                StopTurnAnimation();
                return;
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

                if (Props.AnimationQueue.Count > 0)
                {
                    Logger.LogWarning($"[Combo] {NpcInstance.GetName(NpcNameSlot.Slot0)} cutting anim early → next hit (queue={Props.AnimationQueue.Count})", LogCat.Animation);
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
            var myPosition = NpcContainer.Go.transform.position;
            var targetPosition = _enemyTransform.position;

            var myPositionH = new Vector3(myPosition.x, 0, myPosition.z);
            var targetPositionH = new Vector3(targetPosition.x, 0, targetPosition.z);
            var toTarget = targetPositionH - myPositionH;

            var approachSpread = _configService.Dev.NpcAttackApproachSpread;
            var arrivalThreshold = _configService.Dev.NpcAttackArrivalThreshold;

            // Already within attack range — stop immediately.
            // Without this, approachTarget = targetPos - toTarget.normalized * spread lands BEHIND the
            // enemy when the NPC is already closer than spread, causing it to walk through the player.
            if (toTarget.magnitude <= approachSpread)
            {
                PrefabProps.AnimationSystem.StopAllAnimations();
                IsFinishedFlag = true;
                return;
            }

            // Each attacker targets a point approachSpread from the enemy in its own approach direction.
            // Prevents all NPCs converging on the exact same spot (the "skeleton tower" problem).
            var approachTarget = toTarget.sqrMagnitude > 0.001f
                ? targetPositionH - toTarget.normalized * approachSpread
                : targetPositionH;

            var distance = Vector3.Distance(myPositionH, approachTarget);

            if (distance <= arrivalThreshold)
            {
                PrefabProps.AnimationSystem.StopAllAnimations();
                IsFinishedFlag = true;
                return;
            }

            // Rotate toward actual enemy while running (not toward the offset approach point).
            var guild = NpcInstance.Guild <= (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM ? (int)VmGothicEnums.Guild.GIL_HUMAN : NpcInstance.Guild;
            var turnSpeed = GameStateService.GuildValues.GetTurnSpeed(guild);
            NpcGo.transform.rotation = Quaternion.RotateTowards(
                NpcGo.transform.rotation,
                Quaternion.LookRotation(toTarget),
                Time.deltaTime * turnSpeed);

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
            if (_move != FightAiMove.Attack && _move != FightAiMove.AttackSide)
                return;

            var target = _enemy;
            if (target == null)
            {
                Logger.LogWarning($"[AttackPlayAni] {NpcInstance.GetName(NpcNameSlot.Slot0)} TryFireHit — enemy NpcContainer is null", LogCat.Fight);
                return;
            }

            if (target.Props.BodyState == VmGothicEnums.BodyState.BsDead)
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
            GlobalEventDispatcher.FightHit.Invoke(NpcContainer, target, attackerPos);
        }

        private float GetWeaponReach()
        {
            var baseRange = GameStateService.GuildValues.GetFightRangeBase(Vob.GuildTrue);
            var item = VmCacheService.TryGetItemData(Props.CurrentItem);
            var weaponRange = item?.Range ?? GameStateService.GuildValues.GetFightRangeFist(Vob.GuildTrue);
            return (baseRange + weaponRange) / 100f;
        }
    }
}
