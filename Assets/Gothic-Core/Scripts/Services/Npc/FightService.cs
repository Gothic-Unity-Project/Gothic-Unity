using System.Collections;
using Gothic.Core.Adapters.UI.StatusBars;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
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
        [Inject] private readonly Gothic.Core.Services.GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        public void Init()
        {
            GlobalEventDispatcher.FightHit.AddListener(OnHit);
            GlobalEventDispatcher.SpellHit.AddListener(OnSpellHit);
            GlobalEventDispatcher.FightFinishingMove.AddListener(OnFinishingMove);
        }

        private void OnSpellHit(NpcContainer caster, NpcContainer target, Vector3 pos, int damage)
        {
            Logger.Log($"[FightService.SpellHit] {caster.Instance.GetName(NpcNameSlot.Slot0)} → {target.Instance.GetName(NpcNameSlot.Slot0)} dmg={damage}", LogCat.Fight);
            OnHit(caster, target, pos, damageOverride: damage);
        }

        private void OnHit(NpcContainer attacker, NpcContainer target, Vector3 __) =>
            OnHit(attacker, target, __, damageOverride: null);

        private void OnHit(NpcContainer attacker, NpcContainer target, Vector3 __, int? damageOverride)
        {
            if (target.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                return;

            if (_gameStateService.Dialogs.IsInDialog)
            {
                Logger.Log("[FightService] Hit blocked — dialog in progress", LogCat.Fight);
                return;
            }

            var isHero = target.PrefabProps != null && target.PrefabProps.IsHero();

            // Ignore hits on already-unconscious hero — prevents stacking recovery coroutines.
            if (isHero && target.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return;

            // Hero finishes off unconscious NPC with any hit.
            if (target.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious && !isHero)
            {
                var attackerIsHero = attacker.PrefabProps != null && attacker.PrefabProps.IsHero();
                if (attackerIsHero)
                {
                    Logger.Log($"[FightService] Hero finishes off {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
                    target.Props.BodyState = VmGothicEnums.BodyState.BsDead;
                    OnDyingChangeAnimation(target);
                    OnNpcDied(target, attacker);
                }
                return;
            }

            Logger.Log($"[FightService.OnHit] *** {attacker.Instance.GetName(NpcNameSlot.Slot0)} HIT {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
            if (OnHitUpdateHealth(attacker, target, damageOverride))
            {
                if (!isHero && target.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} finished off while unconscious — DEAD", LogCat.Npc);
                    target.Props.BodyState = VmGothicEnums.BodyState.BsDead;
                    OnDyingChangeAnimation(target);
                    OnNpcDied(target, attacker);
                }
                else if (isHero)
                {
                    Logger.LogWarning("[FightService] Hero knocked out!", LogCat.Fight);
                    OnHeroKnockedOut(target);
                }
                else if (IsHuman(target) && IsHuman(attacker))
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is UNCONSCIOUS", LogCat.Npc);
                    OnNpcKnockedOut(target, attacker);
                }
                else
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is DEAD", LogCat.Npc);
                    target.Props.BodyState = VmGothicEnums.BodyState.BsDead;
                    OnDyingChangeAnimation(target);
                    OnNpcDied(target, attacker);
                }
            }
            else
            {
                Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} took damage, playing hurt animation", LogCat.Npc);
                OnHitChangeAnimation(target);
                OnHitPlaySound(target);
            }

            BroadcastDamagePerceptions(attacker, target);
        }

        /// Fire PERC_ASSESSDAMAGE on the target so it reacts (B_MM_ReactToDamage / B_AssessDamage),
        /// then broadcast PERC_ASSESSOTHERSDAMAGE to nearby NPCs so allies join the fight.
        private void BroadcastDamagePerceptions(NpcContainer attacker, NpcContainer target)
        {
            _npcAiService.ExecutePerception(
                VmGothicEnums.PerceptionType.AssessDamage,
                target.Props, target.Instance,
                victim: target.Instance,
                other: attacker.Instance);

            if (attacker.Go == null) return;

            var broadcasted = 0;
            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                if (candidate == target || candidate == attacker)
                    continue;
                if (candidate.Props.BodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious)
                    continue;
                // Skip culled NPCs — Go is null or inactive (out of cull range).
                if (candidate.Go == null || !candidate.Go.activeInHierarchy)
                    continue;

                // Use candidate's own SensesRange — GetPerceptionRange returns float.MaxValue when unset.
                var sensesRangeM = candidate.Instance.SensesRange / 100f;
                var dist = Vector3.Distance(candidate.Go.transform.position, attacker.Go.transform.position);
                if (dist > sensesRangeM)
                    continue;

                if (candidate.Props.Perceptions.ContainsKey(VmGothicEnums.PerceptionType.AssessOthersDamage))
                {
                    _npcAiService.ExecutePerception(
                        VmGothicEnums.PerceptionType.AssessOthersDamage,
                        candidate.Props, candidate.Instance,
                        victim: target.Instance,
                        other: attacker.Instance);
                }

                if (candidate.Props.Perceptions.ContainsKey(VmGothicEnums.PerceptionType.AssessFightSound))
                {
                    _npcAiService.ExecutePerception(
                        VmGothicEnums.PerceptionType.AssessFightSound,
                        candidate.Props, candidate.Instance,
                        victim: target.Instance,
                        other: attacker.Instance);
                }
                else if (candidate.Instance.Guild >= 16 && candidate.Instance.Guild < 37)
                {
                    // Monsters in routine states don't register the perception — call directly.
                    // Humans must NOT use B_MM_ReactToOthersDamage (monster AI) — their
                    // active PERC_ASSESSENEMY tick will pick up the fight naturally.
                    _npcAiService.CallVmFunctionWithNpcGlobals(
                        "B_MM_REACTTOOTHERSDAMAGE",
                        candidate.Instance,
                        victim: target.Instance,
                        other: attacker.Instance);
                }
                else
                {
                    continue;
                }

                broadcasted++;
            }
            Logger.Log($"[FightService.Perc] AssessOthersDamage broadcast to {broadcasted} NPC(s)", LogCat.Fight);
        }

        // GIL_SEPERATOR_HUM = 16: only true humans (guild < 16) go unconscious.
        // Mole rats (34), orcs (16–37), monsters (>37) all die.
        private static bool IsHuman(NpcContainer npc) => npc.Instance.Guild < 16;

        private void OnNpcKnockedOut(NpcContainer npc, NpcContainer attacker)
        {
            npc.Vob.SetAttribute((int)NpcAttribute.HitPoints, 1);

            var vm = _gameStateService.GothicVm;
            var zsUnconscious = vm.GetSymbolByName("ZS_UNCONSCIOUS");

            if (zsUnconscious == null)
            {
                Logger.LogWarning("[FightService] ZS_UNCONSCIOUS symbol not found — falling back to S_Wounded anim", LogCat.Fight);
                npc.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;
                npc.Props.AnimationQueue.Clear();
                npc.PrefabProps.AnimationSystem.StopAllAnimations();
                _physicsService.DisablePhysicsForNpc(npc.PrefabProps);
                var animName = _animationService.GetAnimationName(VmGothicEnums.AnimationType.UnconsciousA, npc);
                npc.PrefabProps.AnimationSystem.PlayAnimation(animName);
                return;
            }

            // Set AIV_WASDEFEATEDBYSC immediately in C# — StartState is queued so ZS_Unconscious entry
            // runs next frame. Any perception firing before that frame would see AIV=0 and take wrong branch.
            var aivWasDefeated = vm.GetSymbolByName("AIV_WASDEFEATEDBYSC")?.GetInt(0) ?? 19;
            if (attacker.PrefabProps.IsHero())
            {
                npc.Instance.SetAiVar(aivWasDefeated, 1);
                // ZS_Unconscious_Loop transitions via AI_StartState(callEndFunction=false), skipping
                // ZS_Unconscious_End → B_ResetTempAttitude never runs. Reset AttitudeTemp to perm here
                // so PERC_ASSESSENEMY doesn't re-trigger with stale HOSTILE temp after wake-up.
                npc.Vob.AttitudeTemp = npc.Vob.Attitude;
            }

            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = npc.Instance;
            vm.GlobalOther = attacker.Instance;
            // ClearState(false) inside ExtAiStartState resets BodyState=BsStand — set BsUnconscious after.
            _npcAiService.ExtAiStartState(npc.Instance, zsUnconscious.Index, true, "");
            npc.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;

            // Give XP — AIV_WASDEFEATEDBYSC already set above, so B_UnconciousXP won't double-count.
            if (npc.Instance.GetAiVar(aivWasDefeated) == 1)
            {
                var bUnconciousXp = vm.GetSymbolByName("B_UNCONCIOUSXP");
                if (bUnconciousXp != null)
                {
                    vm.Call(bUnconciousXp.Index);
                    _npcService.SyncHeroInstanceToVob();
                    Logger.Log($"[FightService] Unconscious XP given for {npc.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
                }
            }

            vm.GlobalSelf = oldSelf;
            vm.GlobalOther = oldOther;
        }

        private void OnHeroKnockedOut(NpcContainer hero)
        {
            hero.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;
            hero.Vob.SetAttribute((int)NpcAttribute.HitPoints, 1);
            var statusBar = hero.Go.GetComponentInChildren<StatusBarAdapter>(true);
            statusBar?.SetFillAmount(1, hero.Vob.GetAttribute((int)NpcAttribute.HitPointsMax));
            _contextInteractionService.LockPlayerInPlace();
            _unityMonoService.StartCoroutine(KnockoutRecovery(hero));
        }

        private void OnFinishingMove(NpcContainer attacker, NpcContainer target)
        {
            var isHero = target.PrefabProps != null && target.PrefabProps.IsHero();
            if (isHero)
            {
                // No respawn system yet — treat as extended knockout so the player can reload.
                Logger.LogWarning("[FightService.FinishingMove] Hero executed — extended knockout (no respawn yet)", LogCat.Fight);
                if (target.Props.BodyState != VmGothicEnums.BodyState.BsUnconscious)
                    OnHeroKnockedOut(target);
                return;
            }

            Logger.Log($"[FightService.FinishingMove] {attacker.Instance.GetName(NpcNameSlot.Slot0)} executes {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
            target.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            OnDyingChangeAnimation(target);
            OnNpcDied(target, attacker);
        }

        private IEnumerator KnockoutRecovery(NpcContainer hero)
        {
            yield return new WaitForSeconds(10f);
            hero.Props.BodyState = VmGothicEnums.BodyState.BsStand;
            _contextInteractionService.UnlockPlayer();
            Logger.Log("[FightService] Hero recovered from knockout", LogCat.Fight);
        }

        /// <summary>
        /// Handles health changes.
        /// Returns true if Npc/Monster is dead.
        /// </summary>
        private bool OnHitUpdateHealth(NpcContainer attacker, NpcContainer target, int? damageOverride = null)
        {
            // FIXME - Talent/skill level (e.g. 1H skill) is not factored in yet.
            var hitPoints = target.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            var maxHP = target.Vob.GetAttribute((int)NpcAttribute.HitPointsMax);

            int damage;
            if (damageOverride.HasValue)
            {
                // Spell damage bypasses weapon formula — protection still applies.
                var protection = target.Vob.GetProtection((int)DamageType.Fire); // FIXME: use spell damage type
                damage = protection < 0 ? 0 : Mathf.Max(0, damageOverride.Value - protection);
            }
            else
            {
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
                damage = protection < 0 ? 0 : Mathf.Max(0, weaponDamage + strength - protection);
                if (damage <= 0)
                    damage = 10; // debug: force minimum 10 until proper damage calculation is implemented
            }

            // NPC_FLAG_IMMORTAL (bit 1 = 2): take no damage, but combat still plays out normally.
            if (((int)target.Instance.Flags & 2) != 0)
            {
                Logger.Log($"[FightService] {target.Instance.GetName(NpcNameSlot.Slot0)} is immortal — 0 damage", LogCat.Npc);
                damage = 0;
            }

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

            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = dead.Instance;
            // Always set other=hero so Npc_IsPlayer(other) in ZS_Dead fires B_DeathXP,
            // even when a companion/guide NPC landed the killing blow.
            vm.GlobalOther = _npcService.GetHeroContainer().Instance;

            var zsDeadSym = vm.GetSymbolByName("ZS_Dead");
            if (zsDeadSym != null)
            {
                vm.Call(zsDeadSym.Index);
                _npcService.SyncHeroInstanceToVob();
                Logger.Log($"[FightService.OnNpcDied] ZS_Dead: {dead.Instance.GetName(NpcNameSlot.Slot0)} (killer: {killer.Instance.GetName(NpcNameSlot.Slot0)})", LogCat.Npc);
            }
            else
                Logger.LogWarning("[FightService.OnNpcDied] ZS_Dead symbol not found", LogCat.Npc);

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
            if (target.PrefabProps == null || target.PrefabProps.AnimationSystem == null)
                return;

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
