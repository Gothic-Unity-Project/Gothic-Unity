using System.Collections;
using Gothic.Core.Adapters.UI.StatusBars;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Vobs;
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
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly ConfigService _configService;

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

            // Hero is being hit — alert party members immediately. We already know isHero here so no
            // comparison needed; attacker is directly the enemy party members should engage.
            if (isHero)
                AlertPartyMembersToDefendHero(attacker);

            // Any attacker finishes off unconscious NPC — set dead and call ZS_Dead.
            if (target.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious && !isHero)
            {
                Logger.Log($"[FightService] {attacker.Instance.GetName(NpcNameSlot.Slot0)} finishes off unconscious {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
                target.Props.BodyState = VmGothicEnums.BodyState.BsDead;
                OnDyingChangeAnimation(target);
                OnNpcDied(target, attacker);
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
                else if (IsHuman(target) && (attacker.PrefabProps != null && attacker.PrefabProps.IsHero() || IsPartyMember(attacker)))
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
            // Skip on a target that just died from this hit: its registered AssessDamage handler
            // (e.g. ZS_MM_Attack, shared by monsters/summons) unconditionally calls AI_StandUp,
            // which resets BodyState back to BsStand and undoes the death we just set above.
            if (target.Props.BodyState != VmGothicEnums.BodyState.BsDead)
            {
                _npcAiService.ExecutePerception(
                    VmGothicEnums.PerceptionType.AssessDamage,
                    target.Props, target.Instance,
                    victim: target.Instance,
                    other: attacker.Instance);
            }

            var heroContainer = _npcService.GetHeroContainer();
            var heroIndex = heroContainer.Instance.Index;

            // When a non-hero NPC hits a target that is alive, force-switch the target's focus to
            // the attacker. Gothic's B_CombatReactToDamage only does this for the player; we extend
            // it here so that wolves, companions, etc. become the active fight target when they land
            // a hit. Still skip Friendly attackers (e.g. a stray combo hit in a crowd) so allies/guards
            // don't turn hostile on each other from incidental damage — same gate used elsewhere in
            // this file (ActivatePartyMemberAttack) and in NpcAiService's target-switching methods.
            // Same-guild is excluded unconditionally too — this mechanic has no vanilla precedent for
            // guild-mates fighting each other, so don't rely solely on the guild-attitude table for it.
            if (attacker.Instance.Index != heroIndex && target.Props.BodyState != VmGothicEnums.BodyState.BsDead &&
                target.Instance.Guild != attacker.Instance.Guild &&
                _npcAiService.ExtGetAttitude(target.Instance, attacker.Instance) != VmGothicEnums.Attitude.Friendly)
            {
                target.Props.TargetNpc = attacker.Instance;
                target.Props.EnemyNpc = attacker.Instance;
                Logger.Log($"[FightService] {target.Instance.GetName(NpcNameSlot.Slot0)} switches target to {attacker.Instance.GetName(NpcNameSlot.Slot0)} (hit by non-hero)", LogCat.Fight);
            }

            // Party member was hit by a non-hero — it defends itself immediately.
            if (IsPartyMember(target) && attacker.Instance.Index != heroIndex)
            {
                ActivatePartyMemberAttack(target, attacker);
                Logger.Log($"[FightService] Party member {target.Instance.GetName(NpcNameSlot.Slot0)} defends self against {attacker.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
            }

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

                var sensesRangeM = candidate.Instance.SensesRange / 100f;

                // Party members follow the hero — measure distance to hero (anchor), not to attacker.
                if (IsPartyMember(candidate))
                {
                    NpcContainer enemy = null;
                    NpcContainer anchor = null;
                    if (attacker.Instance.Index == heroIndex) { enemy = target; anchor = attacker; }
                    else if (target.Instance.Index == heroIndex) { enemy = attacker; anchor = target; }

                    if (enemy == null || anchor?.Go == null)
                        continue;

                    var distToHero = Vector3.Distance(candidate.Go.transform.position, anchor.Go.transform.position);
                    if (distToHero > sensesRangeM)
                        continue;

                    ActivatePartyMemberAttack(candidate, enemy);
                    broadcasted++;
                    continue;
                }

                var dist = Vector3.Distance(candidate.Go.transform.position, attacker.Go.transform.position);
                if (dist > sensesRangeM)
                    continue;

                // Require visual LOS so NPCs behind walls don't charge through geometry.
                // freeLOS=true: skip FOV check — bystanders can react from any angle as long as there's no wall.
                if (!_npcAiService.ExtNpcCanSeeNpc(candidate.Instance, attacker.Instance, true))
                    continue;

                // If this bystander is locked onto the hero (hidden behind a wall) but can see a
                // non-hero attacker actively fighting right in front of them, redirect their focus
                // immediately. B_CombatReactToDamage only switches target when the PLAYER is the
                // attacker, so without this companions/wolves attacking NPCs are fully ignored.
                // Skip Friendly attackers — otherwise a guard who loses sight of the hero for a
                // moment redirects onto the ally guard standing next to them instead. Same-guild
                // excluded unconditionally too, same reasoning as the force-switch block above.
                if (attacker.Instance.Index != heroIndex &&
                    candidate.Props.TargetNpc?.Index == heroContainer.Instance.Index &&
                    !_npcAiService.ExtNpcCanSeeNpc(candidate.Instance, heroContainer.Instance, false) &&
                    candidate.Instance.Guild != attacker.Instance.Guild &&
                    _npcAiService.ExtGetAttitude(candidate.Instance, attacker.Instance) != VmGothicEnums.Attitude.Friendly)
                {
                    candidate.Props.TargetNpc = attacker.Instance;
                    candidate.Props.EnemyNpc = attacker.Instance;
                    Logger.Log($"[FightService] {candidate.Instance.GetName(NpcNameSlot.Slot0)} switches focus from hidden hero to visible {attacker.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
                }

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

        private bool IsPartyMember(NpcContainer npc)
        {
            var sym = _gameStateService.GothicVm.GetSymbolByName("AIV_MM_PARTYMEMBER");
            if (sym == null) return false;
            var aivIndex = sym.GetInt(0);
            return npc.Instance.GetAiVar(aivIndex) == 1;
        }

        private void AlertPartyMembersToDefendHero(NpcContainer attacker)
        {
            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                if (!IsPartyMember(candidate)) continue;
                if (candidate.Props.BodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious) continue;
                if (candidate.Go == null || !candidate.Go.activeInHierarchy) continue;

                ActivatePartyMemberAttack(candidate, attacker);
            }
        }

        private void ActivatePartyMemberAttack(NpcContainer partyMember, NpcContainer enemy)
        {
            var zsAttack = _gameStateService.GothicVm.GetSymbolByName("ZS_MM_ATTACK");
            if (zsAttack == null) return;

            partyMember.Props.EnemyNpc = enemy.Instance;
            _npcAiService.ExtSetTarget(partyMember.Instance, enemy.Instance);
            var oldOther = _gameStateService.GothicVm.GlobalOther;
            _gameStateService.GothicVm.GlobalOther = enemy.Instance;
            _npcAiService.ExtAiStartState(partyMember.Instance, zsAttack.Index, true, "");
            _gameStateService.GothicVm.GlobalOther = oldOther;
            Logger.Log($"[FightService] Party member {partyMember.Instance.GetName(NpcNameSlot.Slot0)} → attack {enemy.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
        }

        private void OnNpcKnockedOut(NpcContainer npc, NpcContainer attacker)
        {
            DropReadiedWeapon(npc);

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
            if (attacker.PrefabProps.IsHero() || IsPartyMember(attacker))
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
            }

            // Dev cheats: hero one-hit kill / knockout.
            if (attacker.PrefabProps != null && attacker.PrefabProps.IsHero())
            {
                if (_configService.Dev.EnableOneHitKill)
                    damage = maxHP;
                else if (_configService.Dev.EnableOneHitKnockout)
                    damage = Mathf.Max(0, hitPoints - 1);
            }

            // Magic and ranged never one-shot humanoids or the hero — they always survive with 1 HP,
            // requiring a follow-up hit to trigger knockout. Monsters (guild >= 16) take full damage.
            var attackerMode = (VmGothicEnums.WeaponState)attacker.Vob.FightMode;
            var isMagicOrRanged = attackerMode is VmGothicEnums.WeaponState.Mage
                or VmGothicEnums.WeaponState.Bow
                or VmGothicEnums.WeaponState.CBow;
            var targetIsHumanOrHero = (target.PrefabProps != null && target.PrefabProps.IsHero()) || IsHuman(target);
            if (isMagicOrRanged && targetIsHumanOrHero)
                damage = Mathf.Min(damage, Mathf.Max(1, hitPoints - 1));

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
            DropReadiedWeapon(dead);

            // Set flag immediately so AiHandler.Start() on the corpse doesn't double-call ZS_Dead.
            dead.IsZsDeadCalled = true;

            // ZS_Dead (B_CheckDeadMissionNPCs + B_GiveDeathInv etc.) takes ~280ms in G2 — too heavy
            // to run synchronously in the hit frame. Defer by one frame so the frame spike is gone.
            var deadInstance = dead.Instance;
            var heroInstance = _npcService.GetHeroContainer().Instance;
            _unityMonoService.StartCoroutine(CallZsDeadDeferred(deadInstance, heroInstance));

            Logger.Log($"[FightService.OnNpcDied] {dead.Instance.GetName(NpcNameSlot.Slot0)} died — ZS_Dead deferred (killer: {killer.Instance.GetName(NpcNameSlot.Slot0)})", LogCat.Npc);
        }

        private IEnumerator CallZsDeadDeferred(NpcInstance dead, NpcInstance hero)
        {
            yield return null;

            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = dead;
            // Gothic engine always passes hero as 'other' in ZS_Dead regardless of real killer.
            // Scripts like B_CheckDeadMissionNPCs and ZS_Dead XP checks rely on Npc_IsPlayer(other)=true.
            vm.GlobalOther = hero;

            var zsDeadSym = vm.GetSymbolByName("ZS_Dead");
            if (zsDeadSym != null)
            {
                vm.Call(zsDeadSym.Index);
                _npcService.SyncHeroInstanceToVob();
                Logger.Log($"[FightService.OnNpcDied] ZS_Dead complete: {dead.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
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

        private void DropReadiedWeapon(NpcContainer npc)
        {
            if (npc?.Go == null) return;
            var fightMode = (VmGothicEnums.WeaponState)npc.Vob.FightMode;
            var weapon = fightMode switch
            {
                VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H
                    => _npcHelperService.ExtNpcGetEquippedMeleeWeapon(npc.Instance),
                VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow
                    => _npcHelperService.ExtNpcGetEquippedRangedWeapon(npc.Instance),
                _ => null
            };
            if (weapon == null) return;

            Logger.Log($"[DropWeapon] {npc.Instance.GetName(NpcNameSlot.Slot0)} drops '{weapon.Name}' idx={weapon.Index} fightMode={fightMode}", LogCat.Fight);

            _vobService.DropItemAtPosition(weapon.Index, npc.Go.transform.position);

            // Remove from equipped list (visual/logic) so loot menu doesn't show it
            npc.Props.EquippedItems.Remove(weapon);

            // Remove from Daedalus packed inventory so TakeItem→ExtCreateInvItems doesn't duplicate it
            _npcInventoryService.ExtRemoveInvItems(npc.Instance, weapon.Index, 1);
            Logger.Log($"[DropWeapon] removed '{weapon.Name}' from packed inventory", LogCat.Fight);

            // Remove weapon mesh from hand slot so NPC doesn't visually hold it after knockout/death
            var handSlotName = DrawWeapon.GetHandSlotName(fightMode);
            var handGo = npc.Go.FindChildRecursively(handSlotName);
            if (handGo != null && handGo.transform.childCount > 0)
            {
                UnityEngine.Object.Destroy(handGo.transform.GetChild(0).gameObject);
                Logger.Log($"[FightService] DropReadiedWeapon: removed weapon mesh from '{handSlotName}'", LogCat.Fight);
            }

            // Reset FightMode and combat MDS overlay so NPC returns to walk/run stance
            npc.Vob.FightMode = (int)VmGothicEnums.WeaponState.NoWeapon;
            npc.Props.MdsNameOverlay = npc.Props.MdsNameRoutineOverlay;
            npc.Props.CurrentItem = -1;
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

            if (target.PrefabProps == null || target.PrefabProps.NpcSound == null)
                return;
            target.PrefabProps.NpcSound.PlayOneShot(clip);
        }
    }
}
