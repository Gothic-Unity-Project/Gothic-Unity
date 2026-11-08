using Gothic.Core.Const;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Who reacts to a hit: the victim (PERC_ASSESSDAMAGE), the witnesses around the attacker
    /// (PERC_ASSESSOTHERSDAMAGE / PERC_ASSESSFIGHTSOUND, monsters B_MM_ReactToOthersDamage), the hero's party members
    /// (AIV_MM_PARTYMEMBER: ZS_MM_Attack).
    /// </summary>
    public class DamagePerceptionDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;


        /// <summary>
        /// The summoner hits its own summon (weapon, spell, arrow).
        /// </summary>
        public static bool IsOwnSummonHit(NpcContainer attacker, NpcContainer target)
        {
            return target?.SummonedBy != null &&
                   attacker?.Instance != null && target.SummonedBy.Index == attacker.Instance.Index;
        }

        public static bool IsBerzerk(NpcContainer npc)
        {
            return npc?.Vob != null && npc.Vob.CurrentStateName.EqualsIgnoreCase("ZS_Berzerk");
        }

        /// <summary>
        /// What the hero's summon does counts as the hero's - a knockout by
        /// it is the hero's win (ZS_Unconscious checks C_NpcIsHero(other): Buster's admission duel didn't go on after
        /// the hero's goblins beat him), witnesses see the hero as the attacker. Anyone else: the attacker itself.
        /// </summary>
        public NpcContainer GetCreditedAttacker(NpcContainer attacker)
        {
            if (attacker?.SummonedBy == null)
                return attacker;

            var hero = _npcService.GetHeroContainer();
            return hero?.Instance != null && attacker.SummonedBy.Index == hero.Instance.Index ? hero : attacker;
        }

        /// <summary>
        /// Fire PERC_ASSESSDAMAGE on the target so it reacts (B_MM_ReactToDamage / B_AssessDamage),
        /// then broadcast PERC_ASSESSOTHERSDAMAGE to nearby NPCs so allies join the fight.
        /// </summary>
        public void BroadcastDamagePerceptions(NpcContainer attacker, NpcContainer target)
        {
            // A summon is a training dummy for its own summoner -
            // it takes the damage but neither it nor bystanders turn on the summoner.
            if (IsOwnSummonHit(attacker, target))
            {
                Logger.Log($"[DamagePerceptionDomain] {target.Instance.GetName(NpcNameSlot.Slot0)} ignores the hit " +
                           "of its summoner", LogCat.Fight);
                return;
            }

            // Skip on a target that just died from this hit: its registered AssessDamage handler
            // (e.g. ZS_MM_Attack, shared by monsters/summons) unconditionally calls AI_StandUp,
            // which resets BodyState back to BsStand and undoes the death we just set above.
            if (target.Props.BodyState != VmGothicEnums.BodyState.BsDead)
                ReactToDamage(attacker, target);

            var heroContainer = _npcService.GetHeroContainer();
            var heroIndex = heroContainer.Instance.Index;

            // Party member was hit by a non-hero — only defend itself once it has no alive primary
            // target to stay committed to (see HasAlivePrimaryTarget). Otherwise an incidental hit from
            // a bystander would pull it off whatever the hero is actually attacking.
            if (IsPartyMember(target) && attacker.Instance.Index != heroIndex && !HasAlivePrimaryTarget(target))
            {
                ActivatePartyMemberAttack(target, attacker);
                Logger.Log($"[DamagePerceptionDomain] Party member {target.Instance.GetName(NpcNameSlot.Slot0)} " +
                           $"defends self against {attacker.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
            }

            if (attacker.Go == null) return;

            BroadcastToWitnesses(attacker, target, heroContainer);
        }

        /// <summary>
        /// The hero is being hit - his party members without a target of their own attack the attacker.
        /// </summary>
        public void AlertPartyMembersToDefendHero(NpcContainer attacker)
        {
            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            foreach (var candidate in awakeNpcs)
            {
                if (!IsPartyMember(candidate)) continue;
                if (candidate.IsDown()) continue;
                if (candidate.Go == null || !candidate.Go.activeInHierarchy) continue;

                // Fallback only — if the party member is already committed to a live primary target
                // (set by the hero's own attacks, see BroadcastDamagePerceptions), don't pull it off
                // that target just because a different enemy landed a hit on the hero.
                if (HasAlivePrimaryTarget(candidate)) continue;

                ActivatePartyMemberAttack(candidate, attacker);
            }
        }

        /// <summary>
        /// The victim's own reaction: PERC_ASSESSDAMAGE, and a hit interrupts walking.
        /// </summary>
        private void ReactToDamage(NpcContainer attacker, NpcContainer target)
        {
            var isHeroTarget = target.Instance.IsHero(_gameStateService.GothicVm);
            // Diagnostics (a berzerk victim didn't fight back): which damage reaction the victim has in its state.
            if (!isHeroTarget)
                LogDamageReaction(attacker, target);

            // Like the engine's stumble, a hit interrupts walking - the reaction (ZS_ReactToDamage:
            // AI_StartState(ZS_AssessMonster)) queued behind a guide's long AI_GotoWP and never ran: Baal Parvez stood
            // still under a wolf's bites until spoken to.
            if (!isHeroTarget &&
                target.Props.CurrentAction is AbstractWalkAnimationAction or AbstractWalkAnimationAction2)
            {
                Logger.Log($"[DamagePerceptionDomain] {target.Instance.GetName(NpcNameSlot.Slot0)} stops walking - " +
                           $"hit by {attacker.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
                _npcAiService.ExtNpcClearAiQueue(target.Instance);
            }

            _npcAiService.ExecutePerception(
                VmGothicEnums.PerceptionType.AssessDamage,
                target.Props, target.Instance,
                victim: target.Instance,
                other: attacker.Instance);
        }

        private void LogDamageReaction(NpcContainer attacker, NpcContainer target)
        {
            var percFunction = target.Props.Perceptions.TryGetValue(VmGothicEnums.PerceptionType.AssessDamage,
                out var percIndex) && percIndex >= 0
                ? _gameStateService.GothicVm.GetSymbolByIndex(percIndex)?.Name ?? percIndex.ToString()
                : "none";
            Logger.Log($"[DamagePerceptionDomain] AssessDamage on {target.Instance.GetName(NpcNameSlot.Slot0)} " +
                       $"(state {target.Vob.CurrentStateName}) by " +
                       $"{attacker.Instance.GetName(NpcNameSlot.Slot0)}: {percFunction}", LogCat.Fight);
        }

        /// <summary>
        /// Everyone awake (the hero too) who isn't down and senses the hit: party members join the hero's fight,
        /// the others witness it.
        /// </summary>
        private void BroadcastToWitnesses(NpcContainer attacker, NpcContainer target, NpcContainer hero)
        {
            var heroIndex = hero.Instance.Index;
            var creditedAttacker = GetCreditedAttacker(attacker);
            var broadcasted = 0;
            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            for (var awakeIdx = -1; awakeIdx < awakeNpcs.Count; awakeIdx++)
            {
                var candidate = awakeIdx < 0 ? hero : awakeNpcs[awakeIdx];
                if (candidate == null) continue;
                if (candidate == target || candidate == attacker)
                    continue;
                if (candidate.IsDown())
                    continue;
                // Skip culled NPCs — Go is null or inactive (out of cull range).
                if (candidate.Go == null || !candidate.Go.activeInHierarchy)
                    continue;

                var sensesRangeM = candidate.Instance.SensesRange / 100f;

                // Party members follow the hero — measure distance to hero (anchor), not to attacker.
                if (IsPartyMember(candidate))
                {
                    if (TryJoinHeroFight(candidate, attacker, target, heroIndex, sensesRangeM))
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

                TryRefocusFromHiddenHero(candidate, attacker, target, hero);

                if (NotifyWitness(candidate, attacker, target, creditedAttacker))
                    broadcasted++;
            }
            Logger.Log($"[DamagePerceptionDomain] AssessOthersDamage broadcast to {broadcasted} NPC(s)", LogCat.Fight);
        }

        /// <summary>
        /// A party member near the hero attacks whom the hero hit - or, without a target yet, whoever hit the hero.
        /// </summary>
        private bool TryJoinHeroFight(NpcContainer partyMember, NpcContainer attacker, NpcContainer target,
            int heroIndex, float sensesRangeM)
        {
            NpcContainer enemy = null;
            NpcContainer anchor = null;
            if (attacker.Instance.Index == heroIndex)
            {
                // Hero landed the hit — this is the "primary target" signal, always follow it.
                enemy = target;
                anchor = attacker;
            }
            else if (target.Instance.Index == heroIndex && !HasAlivePrimaryTarget(partyMember))
            {
                // Hero got hit instead — only a fallback for "no target yet," not an override,
                // so the party member doesn't ping-pong onto whoever last traded blows with the
                // hero in a multi-enemy fight.
                enemy = attacker;
                anchor = target;
            }

            if (enemy == null || anchor?.Go == null)
                return false;

            var distToHero = Vector3.Distance(partyMember.Go.transform.position, anchor.Go.transform.position);
            if (distToHero > sensesRangeM)
                return false;

            ActivatePartyMemberAttack(partyMember, enemy);
            return true;
        }

        /// <summary>
        /// If this bystander is locked onto the hero (hidden behind a wall) but can see a
        /// non-hero attacker actively fighting right in front of them, redirect their focus
        /// immediately. B_CombatReactToDamage only switches target when the PLAYER is the
        /// attacker, so without this companions/wolves attacking NPCs are fully ignored.
        /// Skip Friendly attackers — otherwise a guard who loses sight of the hero for a
        /// moment redirects onto the ally guard standing next to them instead. Same-guild
        /// excluded unconditionally too (no vanilla precedent for guild-mates fighting each other).
        /// Party members excluded as well — a bystander that's lost sight of the hero should
        /// keep trying to reacquire the hero, not settle on the summon that's visibly fighting
        /// nearby instead.
        /// Not when the attacker hits the hero himself - he's on the bystander's side (Rangar beat the hero,
        /// the citizen fighting the hero turned on Rangar).
        /// </summary>
        private void TryRefocusFromHiddenHero(NpcContainer candidate, NpcContainer attacker, NpcContainer target,
            NpcContainer hero)
        {
            var heroIndex = hero.Instance.Index;
            if (attacker.Instance.Index == heroIndex || target.Instance.Index == heroIndex ||
                candidate.Props.TargetNpc?.Index != heroIndex ||
                _npcAiService.ExtNpcCanSeeNpc(candidate.Instance, hero.Instance, false) ||
                candidate.Instance.Guild == attacker.Instance.Guild ||
                IsPartyMember(attacker) ||
                _npcAiService.ExtGetAttitude(candidate.Instance, attacker.Instance) == VmGothicEnums.Attitude.Friendly)
                return;

            candidate.Props.TargetNpc = attacker.Instance;
            candidate.Props.EnemyNpc = attacker.Instance;
            Logger.Log($"[DamagePerceptionDomain] {candidate.Instance.GetName(NpcNameSlot.Slot0)} switches focus " +
                       $"from hidden hero to visible {attacker.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
        }

        /// <summary>
        /// The witness's perceptions of the hit. Bystanders blame the hero for his summon's hits (creditedAttacker) -
        /// guards go for him; the victim itself still fights back against the summon. False: it has no reaction.
        /// </summary>
        private bool NotifyWitness(NpcContainer candidate, NpcContainer attacker, NpcContainer target,
            NpcContainer creditedAttacker)
        {
            if (candidate.Props.Perceptions.ContainsKey(VmGothicEnums.PerceptionType.AssessOthersDamage))
            {
                _npcAiService.ExecutePerception(
                    VmGothicEnums.PerceptionType.AssessOthersDamage,
                    candidate.Props, candidate.Instance,
                    victim: target.Instance,
                    other: creditedAttacker.Instance);
            }

            if (candidate.Props.Perceptions.ContainsKey(VmGothicEnums.PerceptionType.AssessFightSound))
            {
                _npcAiService.ExecutePerception(
                    VmGothicEnums.PerceptionType.AssessFightSound,
                    candidate.Props, candidate.Instance,
                    victim: target.Instance,
                    other: creditedAttacker.Instance);
                return true;
            }

            if (_gameStateService.IsMonsterGuild(candidate.Instance.Guild))
            {
                // Monsters in routine states don't register the perception — call directly.
                // Humans must NOT use B_MM_ReactToOthersDamage (monster AI) — their
                // active PERC_ASSESSENEMY tick will pick up the fight naturally.
                _npcAiService.CallVmFunctionWithNpcGlobals(
                    DaedalusConst.BMmReactToOthersDamage,
                    candidate.Instance,
                    victim: target.Instance,
                    other: attacker.Instance);
                return true;
            }

            return false;
        }

        private bool IsPartyMember(NpcContainer npc)
        {
            var sym = _gameStateService.GothicVm.GetSymbolByName(DaedalusConst.AivMmPartyMember);
            if (sym == null) return false;
            var aivIndex = sym.GetInt(0);
            return npc.Instance.GetAiVar(aivIndex) == 1;
        }

        /// <summary>
        /// A party member's "primary target" is whatever the hero is actively attacking — sticky until
        /// that target goes down, so a summon doesn't ping-pong between every enemy that trades hits
        /// with the hero in a multi-enemy fight (logs showed exactly this: "Party member Golem → attack
        /// Strażnik" / "→ attack Diego" alternating every few seconds as different guards traded blows
        /// with the hero). Self-defense (reacting to whoever's hitting the party member directly) is
        /// only allowed once there's no alive primary target to stay committed to.
        /// </summary>
        private static bool HasAlivePrimaryTarget(NpcContainer partyMember)
        {
            var current = partyMember.Props.TargetNpc?.GetUserData();
            return current != null &&
                   current.Props.BodyState != VmGothicEnums.BodyState.BsDead &&
                   current.Props.BodyState != VmGothicEnums.BodyState.BsUnconscious;
        }

        private void ActivatePartyMemberAttack(NpcContainer partyMember, NpcContainer enemy)
        {
            var zsAttack = _gameStateService.GothicVm.GetSymbolByName(DaedalusConst.ZsMmAttack);
            if (zsAttack == null) return;

            partyMember.Props.EnemyNpc = enemy.Instance;
            _npcAiService.ExtSetTarget(partyMember.Instance, enemy.Instance);
            var oldOther = _gameStateService.GothicVm.GlobalOther;
            _gameStateService.GothicVm.GlobalOther = enemy.Instance;
            try
            {
                _npcAiService.ExtAiStartState(partyMember.Instance, zsAttack.Index, true, "");
            }
            finally
            {
                _gameStateService.GothicVm.GlobalOther = oldOther;
            }
            Logger.Log($"[DamagePerceptionDomain] Party member {partyMember.Instance.GetName(NpcNameSlot.Slot0)} → " +
                       $"attack {enemy.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
        }
    }
}
