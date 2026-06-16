using Gothic.Core.Adapters.UI.StatusBars;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    public class FightService
    {
        [Inject] private AudioService _audioService;
        [Inject] private AnimationService _animationService;
        [Inject] private PhysicsService _physicsService;
        [Inject] private NpcHelperService _npcHelperService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly Gothic.Core.Services.GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;

        public void Init()
        {
            if (!_configService.Dev.EnableCombatSystem)
                return;

            GlobalEventDispatcher.FightHit.AddListener(OnHit);
        }

        private void OnHit(NpcContainer attacker, NpcContainer target, Vector3 __)
        {
            if (target.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                return;

            Logger.Log($"[FightService.OnHit] *** {attacker.Instance.GetName(NpcNameSlot.Slot0)} HIT {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
            if (OnHitUpdateHealth(attacker, target))
            {
                Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is DEAD", LogCat.Npc);
                target.Props.BodyState = VmGothicEnums.BodyState.BsDead;
                OnDyingChangeAnimation(target);
                if (_configService.Dev.EnableDeathXP)
                    OnNpcDied(target, attacker);
            }
            else
            {
                Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} took damage, playing hurt animation", LogCat.Npc);
                OnHitChangeAnimation(target);
                OnHitPlaySound(target);
            }
        }

        /// <summary>
        /// Handles health changes.
        /// Returns true if Npc/Monster is dead.
        /// </summary>
        private bool OnHitUpdateHealth(NpcContainer attacker, NpcContainer target)
        {
            // FIXME - Talent/skill level (e.g. 1H skill) is not factored in yet.
            var hitPoints = target.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            var maxHP = target.Vob.GetAttribute((int)NpcAttribute.HitPointsMax);

            var equippedWeapon = _npcHelperService.ExtNpcGetEquippedMeleeWeapon(attacker.Instance);

            // G1 melee damage: weapon damage + strength, reduced by the protection matching the damage type.
            // Unarmed attackers (fists, monster claws/bites) deal blunt damage with their strength alone.
            var strength = attacker.Vob.GetAttribute((int)NpcAttribute.Strength);
            var weaponDamage = equippedWeapon?.DamageTotal ?? 0;
            var protectionIndex = equippedWeapon == null
                ? (int)DamageType.Blunt
                : GetProtectionIndex(equippedWeapon.DamageType);
            var protection = target.Vob.GetProtection(protectionIndex);

            // Like G1: protection -1 means immune to this damage type; otherwise no damage when fully absorbed.
            var damage = protection < 0 ? 0 : Mathf.Max(0, weaponDamage + strength - protection);
            if (damage <= 0)
                damage = 10; // debug: force minimum 10 until proper damage calculation is implemented

            Logger.Log($"[FightService.OnHitUpdateHealth] {target.Instance.GetName(NpcNameSlot.Slot0)}: {hitPoints} - {damage} dmg", LogCat.Npc);

            hitPoints -= damage;

            Logger.Log($"[FightService.OnHitUpdateHealth] {target.Instance.GetName(NpcNameSlot.Slot0)} HP after: {hitPoints}/{maxHP}", LogCat.Npc);

            target.Vob.SetAttribute((int)NpcAttribute.HitPoints, hitPoints);

            var statusBar = target.Go.GetComponentInChildren<StatusBarAdapter>(true);
            if (statusBar != null)
            {
                Logger.Log($"[FightService.OnHitUpdateHealth] Updating HP bar for {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
                statusBar.SetFillAmount(hitPoints, maxHP);
            }
            else
            {
                Logger.LogWarning($"[FightService.OnHitUpdateHealth] No StatusBar found for {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
            }

            return hitPoints <= 0;
        }

        private void OnNpcDied(NpcContainer dead, NpcContainer killer)
        {
            var vm = _gameStateService.GothicVm;
            var aivPlundered = vm.GetSymbolByName("AIV_PLUNDERED")?.GetInt(0) ?? 8;
            dead.Instance.SetAiVar(aivPlundered, 0);

            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = dead.Instance;
            vm.GlobalOther = killer.Instance;

            if (_configService.Dev.EnableDeathXP && killer.PrefabProps.IsHero())
            {
                var bDeathXp = vm.GetSymbolByName("B_DeathXP");
                if (bDeathXp != null)
                {
                    vm.Call(bDeathXp.Index);
                    _npcService.SyncHeroInstanceToVob();
                    Logger.Log($"[FightService.OnNpcDied] B_DeathXP: {dead.Instance.GetName(NpcNameSlot.Slot0)} killed by hero", LogCat.Npc);
                }
            }

            var bGiveDeathInv = vm.GetSymbolByName("B_GiveDeathInv");
            if (bGiveDeathInv != null)
                vm.Call(bGiveDeathInv.Index);

            vm.GlobalSelf = oldSelf;
            vm.GlobalOther = oldOther;
        }

        /// <summary>
        /// C_ITEM.damageType is a DAM_* bitmask whose bit positions match the PROT_* indices.
        /// Weapons carry one damage type; the first set bit wins.
        /// </summary>
        private static int GetProtectionIndex(int damageTypeMask)
        {
            for (var i = 0; i < 8; i++)
            {
                if ((damageTypeMask & (1 << i)) != 0)
                    return i;
            }

            return (int)DamageType.Blunt;
        }

        private void OnDyingChangeAnimation(NpcContainer target)
        {
            // Clear pending AI queue and stop all running animations (e.g. s_walk still looping).
            // Death takes priority over everything — bypass the queue and play directly.
            target.Props.AnimationQueue.Clear();
            target.PrefabProps.AnimationSystem.StopAllAnimations();
            _physicsService.DisablePhysicsForNpc(target.PrefabProps);

            // Drop any queued non-death actions (e.g. a GoToWp/UseMob enqueued the same frame the killing hit
            // landed). Otherwise AiHandler's dead-NPC branch would play them out on the corpse before dying.
            target.Props.AnimationQueue.Clear();

            var animName = _animationService.GetAnimationName(VmGothicEnums.AnimationType.DeadB, target);
            target.PrefabProps.AnimationSystem.PlayAnimation(animName);
        }

        private void OnHitChangeAnimation(NpcContainer target)
        {
            // Play hurt on top of whatever is currently running — don't interrupt the current action.
            var animName = _animationService.GetAnimationName(VmGothicEnums.AnimationType.StumbleA, target);
            target.PrefabProps.AnimationSystem.PlayAnimation(animName);
        }

        private void OnHitPlaySound(NpcContainer target)
        {
            // In G1, Humans needs to have stumble sound called via Aargh svm.
            var clip = _audioService.GetRandomSoundClip($"SVM_{target.Instance.Voice}_AARGH");

            // Monsters will have their stumble sound inside animations itself.
            if (clip == null)
                return;

            target.PrefabProps.NpcSound.PlayOneShot(clip);
        }
    }
}
