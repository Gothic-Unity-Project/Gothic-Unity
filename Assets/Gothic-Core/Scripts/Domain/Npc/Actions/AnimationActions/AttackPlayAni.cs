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


        public AttackPlayAni(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Tick()
        {
            base.Tick();

            if (IsFinishedFlag)
                return;

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
            }
        }

        private void RunTick()
        {
            var myPosition = NpcContainer.Go.transform.position;
            var targetPosition = _enemyTransform.position;

            // Consider only horizontal distance (ignore Y-axis)
            var myPositionHorizontal = new Vector3(myPosition.x, 0, myPosition.z);
            var targetPositionHorizontal = new Vector3(targetPosition.x, 0, targetPosition.z);
            var distance = Vector3.Distance(myPositionHorizontal, targetPositionHorizontal);

            if (distance <= 1f)
            {
                PrefabProps.AnimationSystem.StopAllAnimations();
                IsFinishedFlag = true;
                return;
            }

            // Rotate toward enemy using the guild-defined turn speed — same as StrafeTick() and the original Gothic engine.
            var guild = NpcInstance.Guild <= (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM ? (int)VmGothicEnums.Guild.GIL_HUMAN : NpcInstance.Guild;
            var turnSpeed = GameStateService.GuildValues.GetTurnSpeed(guild);
            var direction = targetPositionHorizontal - myPositionHorizontal;
            NpcGo.transform.rotation = Quaternion.RotateTowards(
                NpcGo.transform.rotation,
                Quaternion.LookRotation(direction),
                Time.deltaTime * turnSpeed);
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

            // Player uses VR physical collision — bone collider detection handles NPC→player hits.
            if (target.PrefabProps != null && target.PrefabProps.IsHero())
                return;

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
