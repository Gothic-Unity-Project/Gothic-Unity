using Gothic.Core.Const;
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
    /// Who an NPC fights or looks at, picked from the hero and the awake NPCs (AiHandler's perception tick and
    /// Npc_GetNextTarget): the next target, the enemy for PERC_ASSESSENEMY, switching to someone attacking it, the
    /// armed NPC nearby (DeveloperConfig gate in AiHandler) and the closest body for PERC_ASSESSBODY.
    /// Owned by NpcAiService, whose attitude/sight/state externals it uses.
    /// </summary>
    public class NpcTargetSelectionDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;

        private readonly NpcAiService _npcAiService;


        public NpcTargetSelectionDomain(NpcAiService npcAiService)
        {
            _npcAiService = npcAiService;
        }

        /// <summary>
        /// Npc_GetNextTarget: the closest hostile NPC within senses range becomes target, enemy and >other<.
        /// </summary>
        public int FindNextTarget(NpcInstance npc)
        {
            var selfNpc = npc.GetUserData();
            var selfPosition = selfNpc.Go.transform.position;

            NpcContainer closestEnemy = null;
            var closestSqrDist = float.MaxValue;

            var sensesRangeMeters = npc.SensesRange / 100f;
            var sensesRangeSqr = sensesRangeMeters * sensesRangeMeters;

            var enemyOverrideAivar = _gameStateService.GothicVm.GetSymbolByName(DaedalusConst.AivEnemyOverride)
                ?.GetInt(0) ?? -1;

            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            var awakeHero = _gameStateService.GothicVm.GetHeroContainer();
            for (var awakeIdx = -1; awakeIdx < awakeNpcs.Count; awakeIdx++)
            {
                var candidate = awakeIdx < 0 ? awakeHero : awakeNpcs[awakeIdx];
                if (candidate == null) continue;
                if (candidate.Props == null || candidate.Go == null)
                    continue;
                if (candidate.Instance.Index == npc.Index)
                    continue;
                if (NpcHelperService.IsDown(candidate))
                    continue;

                var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
                if (sqrDist > sensesRangeSqr || sqrDist >= closestSqrDist)
                    continue;

                // Party members (summons/allies) are never a valid auto-acquired target, full stop —
                // not even "unless they already provoked me." That carve-out used to check whether
                // self's OWN target field already pointed at the candidate, which is trivially true
                // once something else has already (wrongly) force-switched onto them, so it let a
                // summon get picked right back up as "next target" after the bug that put it there in
                // the first place. NPCs should always re-focus the hero (the real aggressor), not a
                // summon that's just piling on damage alongside them.
                if (IsPartyMember(candidate.Instance))
                    continue;

                // Same-guild NPCs are never a valid auto-acquired target — vanilla never exercises
                // Npc_GetNextTarget() against a fellow guild member, so an under-specified guild-attitude
                // entry (or a leftover personal attitude flag) resolving to Hostile between two guards
                // has no vanilla behavior to fall back on. Safety net regardless of the attitude table.
                if (candidate.Instance.Guild == npc.Guild)
                    continue;

                // A summon never targets its own summoner, even when their guilds differ (e.g. a
                // SkeletonMage is GIL_DEMON so it "flies" instead of wading, but its GIL_SKELETON
                // summons don't share that guild) — see NpcContainer.SummonedBy.
                if (selfNpc.SummonedBy != null && selfNpc.SummonedBy.Index == candidate.Instance.Index)
                    continue;

                if (_npcAiService.ExtGetAttitude(npc, candidate.Instance) != VmGothicEnums.Attitude.Hostile)
                    continue;

                // DeveloperConfig.EnableEnemyOverrideNextTarget (G2): AIV_EnemyOverride NPCs (the bandits on the road
                // below Xardas' tower) leave the guild-hostile hero alone until he talks to them (B_AssessEnemy skips
                // them). After killing a wolf, ZS_Attack_Loop's Npc_GetNextTarget picked the bystanding hero.
                if (enemyOverrideAivar >= 0 && candidate.PrefabProps != null && candidate.PrefabProps.IsHero() &&
                    npc.GetAiVar(enemyOverrideAivar) != 0)
                    continue;

                closestSqrDist = sqrDist;
                closestEnemy = candidate;
            }

            if (closestEnemy == null)
            {
                Logger.LogEditor($"[GetNextTarget] {npc.GetName(NpcNameSlot.Slot0)}: no next target", LogCat.Fight);
                return 0;
            }

            selfNpc.Props.TargetNpc = closestEnemy.Instance;
            selfNpc.Props.EnemyNpc = closestEnemy.Instance;
            _gameStateService.GothicVm.GlobalOther = closestEnemy.Instance;
            Logger.LogEditor($"[GetNextTarget] {npc.GetName(NpcNameSlot.Slot0)} → " +
                $"{closestEnemy.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
            return 1;
        }

        /// <summary>
        /// PERC_ASSESSBODY's other: the closest dead NPC the NPC senses (senses_range, sight/hearing like enemies).
        /// </summary>
        public NpcInstance FindClosestSensedBody(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            if (selfNpc?.Go == null)
                return null;

            var selfPosition = selfNpc.Go.transform.position;
            var sensesRangeMeters = self.SensesRange / 100f;
            var closestSqrDist = sensesRangeMeters * sensesRangeMeters;
            NpcContainer closest = null;

            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            var awakeHero = _gameStateService.GothicVm.GetHeroContainer();
            for (var awakeIdx = -1; awakeIdx < awakeNpcs.Count; awakeIdx++)
            {
                var candidate = awakeIdx < 0 ? awakeHero : awakeNpcs[awakeIdx];
                if (candidate == null) continue;
                if (candidate == selfNpc || candidate.Props == null || candidate.Go == null) continue;
                if (!candidate.Props.IsDead) continue;

                var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
                if (sqrDist > closestSqrDist) continue;
                if (!_npcHelperService.CanSenseNpc(self, candidate.Instance, false)) continue;

                closestSqrDist = sqrDist;
                closest = candidate;
            }

            return closest?.Instance;
        }

        public void UpdateEnemyNpc(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            var selfPosition = selfNpc.Go.transform.position;

            var sensesRangeMeters = self.SensesRange / 100f;
            var sensesRangeSqr = sensesRangeMeters * sensesRangeMeters;

            // Keep the current target if it's still a valid hostile, instead of re-scanning "closest
            // hostile" completely from scratch every tick. Without this, any momentary crossover in
            // relative distance between two similarly-far candidates flips the target — and once
            // flipped, the NPC's already-active combat state just keeps pursuing the new one, with no
            // way back. Confirmed via logs: scavengers that first reacted to the hero permanently
            // switched to attacking a nearby bystander NPC once it became marginally closer, and never
            // reconsidered even though the hero (the actual provoker) stayed just as close.
            var currentTarget = selfNpc.Props.EnemyNpc;
            if (currentTarget != null)
            {
                var currentTargetNpc = currentTarget.GetUserData();
                // Unconscious excluded too, not just dead — an incapacitated target isn't an ongoing
                // fight to keep confirming every tick (matches FindNextTarget, which already
                // excludes both states).
                var stillValid = currentTargetNpc != null &&
                                  currentTargetNpc.Go != null &&
                                  !NpcHelperService.IsDown(currentTargetNpc) &&
                                  currentTarget.Guild != self.Guild &&
                                  !IsPartyMember(currentTarget) &&
                                  (selfNpc.SummonedBy == null || selfNpc.SummonedBy.Index != currentTarget.Index) &&
                                  _npcAiService.ExtGetAttitude(self, currentTarget) == VmGothicEnums.Attitude.Hostile &&
                                  (currentTargetNpc.Go.transform.position - selfPosition).sqrMagnitude <= sensesRangeSqr &&
                                  _npcHelperService.CanSenseNpc(self, currentTarget, true);
                if (stillValid)
                {
                    selfNpc.Props.TargetNpc = currentTarget;
                    return;
                }
            }

            NpcContainer closestEnemy = null;
            var closestSqrDist = float.MaxValue;

            // Only the hero and the NPCs that are awake: asleep NPCs are far away and frozen. Scanning every NPC of
            // the world on each perception made crowded places stutter (Old Camp: 52 awake of 992).
            var hero = _gameStateService.GothicVm.GetHeroContainer();
            if (hero != null && IsCloserEnemy(selfNpc, selfPosition, hero, sensesRangeSqr, ref closestSqrDist))
                closestEnemy = hero;

            foreach (var candidate in _npcMeshCullingService.AwakeNpcs)
            {
                if (IsCloserEnemy(selfNpc, selfPosition, candidate, sensesRangeSqr, ref closestSqrDist))
                    closestEnemy = candidate;
            }

            // Mirror into TargetNpc too (consumed by ZS_Attack_Loop's Npc_GetTarget()). Must also be
            // cleared to null when no hostile is found — otherwise a stale TargetNpc (e.g. hero, from
            // before this NPC's attitude was changed to Friendly/Neutral) keeps being picked up by the
            // Daedalus loop forever since Npc_GetTarget() never re-checks attitude itself.
            selfNpc.Props.EnemyNpc = closestEnemy?.Instance;
            selfNpc.Props.TargetNpc = closestEnemy?.Instance;
        }

        /// <summary>
        /// The candidate is a hostile NPC self senses, closer than the closest one so far (which it then becomes).
        /// </summary>
        private bool IsCloserEnemy(NpcContainer selfNpc, Vector3 selfPosition, NpcContainer candidate,
            float sensesRangeSqr, ref float closestSqrDist)
        {
            var self = selfNpc.Instance;

            // Fast-fail checks in order of cheapest first
            if (candidate.Props == null || candidate.Go == null)
                return false;

            if (candidate.Instance.Index == self.Index)
                return false;

            // Corpses aren't enemies, and neither is someone currently knocked out.
            if (NpcHelperService.IsDown(candidate))
                return false;

            // Range and closest-so-far gates before the expensive attitude and senses checks
            // (senses may include a line-of-sight raycast for see-only monsters).
            var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
            if (sqrDist > sensesRangeSqr || sqrDist >= closestSqrDist)
                return false;

            // Safety net regardless of the guild-attitude table — see FindNextTarget.
            if (candidate.Instance.Guild == self.Guild)
                return false;

            // Party members (summons/allies) never a valid auto-acquired target — see FindNextTarget.
            if (IsPartyMember(candidate.Instance))
                return false;

            // A summon never targets its own summoner — see FindNextTarget / NpcContainer.SummonedBy.
            if (selfNpc.SummonedBy != null && selfNpc.SummonedBy.Index == candidate.Instance.Index)
                return false;

            if (_npcAiService.ExtGetAttitude(self, candidate.Instance) != VmGothicEnums.Attitude.Hostile)
                return false;

            // Hearing/smell detect through walls (matches vanilla — e.g. a sleeping molerat wakes
            // to nearby footsteps without seeing the source); only SENSE_SEE requires LOS+FOV.
            // CanSenseNpc(freeLOS=true) handles that split internally.
            if (!_npcHelperService.CanSenseNpc(self, candidate.Instance, true))
                return false;

            closestSqrDist = sqrDist;
            return true;
        }
        /// <summary>
        /// Switches focus to a nearby NPC/monster that is actively fighting >self< (has self as their
        /// own TargetNpc/EnemyNpc), even when self's current target is hostile-by-guild-table but out of
        /// sight (e.g. the hero heard through a wall). This covers companions (Cavalorn) and monsters
        /// (wolves) engaging in melee, whose guild relation to self isn't marked Hostile in the guild
        /// table. Unlike <see cref="UpdateEnemyNpc"/>, this is NOT gated behind PERC_ASSESSENEMY being
        /// registered — combat states like ZS_Attack don't register that perception at all, so without
        /// this NPCs already locked onto the hero would never notice someone else engaging them in melee.
        /// </summary>
        public void UpdateActiveAttackerTarget(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            if (selfNpc?.Go == null)
                return;

            // Keep the current target if it's still alive and visible — avoids thrashing away from a
            // valid fight once LOS to it is (re)established.
            var currentTarget = selfNpc.Props.TargetNpc;
            if (currentTarget != null)
            {
                var currentTargetNpc = currentTarget.GetUserData();
                if (currentTargetNpc != null &&
                    currentTargetNpc.Props.BodyState != VmGothicEnums.BodyState.BsDead &&
                    _npcAiService.ExtNpcCanSeeNpc(self, currentTarget, false))
                    return;
            }

            var selfPosition = selfNpc.Go.transform.position;
            var sensesRangeMeters = self.SensesRange / 100f;
            var sensesRangeSqr = sensesRangeMeters * sensesRangeMeters;

            NpcContainer attacker = null;
            var closestSqrDist = float.MaxValue;

            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            var awakeHero = _gameStateService.GothicVm.GetHeroContainer();
            for (var awakeIdx = -1; awakeIdx < awakeNpcs.Count; awakeIdx++)
            {
                var candidate = awakeIdx < 0 ? awakeHero : awakeNpcs[awakeIdx];
                if (candidate == null) continue;
                if (candidate.Props == null || candidate.Go == null) continue;
                if (candidate.Instance.Index == self.Index) continue;
                if (candidate.Props.BodyState == VmGothicEnums.BodyState.BsDead) continue;

                // Candidate must currently be fighting self (has self as their own target/enemy).
                var targetIdx = candidate.Props.TargetNpc?.Index ?? -1;
                var enemyIdx = candidate.Props.EnemyNpc?.Index ?? -1;
                if (targetIdx != self.Index && enemyIdx != self.Index) continue;

                // Never auto-switch onto someone explicitly Friendly to self (e.g. an NPC the player
                // pacified/saved earlier) even if a stale target reference points at self. Same-guild
                // is excluded unconditionally too — safety net regardless of the guild-attitude table.
                // Party members excluded unconditionally as well, even though the precondition above
                // already means one is "actively fighting self" — a summon landing incidental hits on
                // an NPC that's really fighting the hero shouldn't steal that NPC's focus (see FindNextTarget).
                if (_npcAiService.ExtGetAttitude(self, candidate.Instance) == VmGothicEnums.Attitude.Friendly) continue;
                if (candidate.Instance.Guild == self.Guild) continue;
                if (IsPartyMember(candidate.Instance)) continue;
                // A summon never targets its own summoner — see FindNextTarget / NpcContainer.SummonedBy.
                if (selfNpc.SummonedBy != null && selfNpc.SummonedBy.Index == candidate.Instance.Index) continue;

                var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
                if (sqrDist > sensesRangeSqr || sqrDist >= closestSqrDist) continue;
                // Hearing/smell detect through walls, matching the "heard through a wall" case this
                // method is meant to cover (see doc comment above) — only SENSE_SEE requires LOS+FOV.
                if (!_npcHelperService.CanSenseNpc(self, candidate.Instance, true)) continue;

                closestSqrDist = sqrDist;
                attacker = candidate;
            }

            if (attacker == null)
                return;

            selfNpc.Props.EnemyNpc = attacker.Instance;
            selfNpc.Props.TargetNpc = attacker.Instance;
        }

        // B_AssessFighter.d has "if (!Npc_IsPlayer(other)) return" — so vanilla Daedalus never
        // makes NPCs react to an armed non-player NPC approaching them. We add that missing
        // NPC→NPC armed-threat detection here: if an NPC with drawn weapon is actively engaged
        // in combat (TargetNpc set) and visible within ~4m, self enters ZS_AssessFighter just
        // as it would if the player had drawn a weapon nearby.
        public void CheckForArmedNpcThreat(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            if (selfNpc?.Go == null) return;

            const float threatRangeMeters = 4f; // slightly beyond HAI_DIST_MELEE (~3m)
            var selfPos = selfNpc.Go.transform.position;

            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            foreach (var candidate in awakeNpcs)
            {
                if (candidate.Props == null || candidate.Go == null) continue;
                if (candidate.Instance.Index == self.Index) continue;
                if (candidate.Props.BodyState == VmGothicEnums.BodyState.BsDead) continue;
                // The hero is Daedalus' job (PERC_ASSESSFIGHTER, its warnings escalate). With the hero's fight target
                // still set after a hit, this restarted ZS_AssessFighter every tick - the first warning forever.
                if (candidate.PrefabProps != null && candidate.PrefabProps.IsHero()) continue;

                var weaponState = (VmGothicEnums.WeaponState)candidate.Vob.FightMode;
                if (weaponState == VmGothicEnums.WeaponState.NoWeapon ||
                    weaponState == VmGothicEnums.WeaponState.Fist) continue;

                // Only react to NPCs actively engaged in combat, not patrol NPCs holding ranged weapons.
                if (candidate.Props.TargetNpc == null) continue;

                if (Vector3.Distance(selfPos, candidate.Go.transform.position) > threatRangeMeters) continue;

                // Mirror B_AssessFighter: ignore friendly NPCs (e.g. allies training), react to all others.
                // Same-guild excluded unconditionally too — this mechanic has no vanilla precedent for
                // guild-mates fighting each other, so don't rely solely on the guild-attitude table for it.
                // Party members excluded too — a summon actively fighting nearby shouldn't itself read as
                // an "armed threat" to bystanders; they should still only ever focus the hero.
                if (_npcAiService.ExtGetAttitude(self, candidate.Instance) == VmGothicEnums.Attitude.Friendly) continue;
                if (candidate.Instance.Guild == self.Guild) continue;
                if (IsPartyMember(candidate.Instance)) continue;
                // A summon never treats its own summoner as an armed threat — see FindNextTarget / NpcContainer.SummonedBy.
                if (selfNpc.SummonedBy != null && selfNpc.SummonedBy.Index == candidate.Instance.Index) continue;

                if (!_npcAiService.ExtNpcCanSeeNpc(self, candidate.Instance, false)) continue;

                var zsAssessFighterSym = _gameStateService.GothicVm.GetSymbolByName(DaedalusConst.ZsAssessFighter);
                if (zsAssessFighterSym == null) return;
                // Already sizing someone up - restarting the state would reset its warnings.
                if (selfNpc.Vob.CurrentStateName?.StartsWithIgnoreCase("ZS_ASSESSFIGHTER") == true) return;

                selfNpc.Props.EnemyNpc = candidate.Instance;
                selfNpc.Props.TargetNpc = candidate.Instance;
                Logger.Log($"[NpcAiService] {self.GetName(NpcNameSlot.Slot0)} reacts to armed {candidate.Instance.GetName(NpcNameSlot.Slot0)} — ZS_AssessFighter", LogCat.Fight);

                var oldOther = _gameStateService.GothicVm.GlobalOther;
                _gameStateService.GothicVm.GlobalOther = candidate.Instance;
                _npcAiService.ExtAiStartState(self, zsAssessFighterSym.Index, false, "");
                _gameStateService.GothicVm.GlobalOther = oldOther;
                break;
            }
        }

        // Shared by every auto target-acquisition heuristic above — a party member (summon/ally) should
        // never be picked up as a new/replacement target by these, only ever by the deliberate combat
        // flow (ActivatePartyMemberAttack) that mirrors the hero's own target.
        private bool IsPartyMember(NpcInstance npc)
        {
            var partySymbol = _gameStateService.GothicVm.GetSymbolByName(DaedalusConst.AivMmPartyMember);
            if (partySymbol == null) return false;
            return npc.GetAiVar(partySymbol.GetInt(0)) == 1;
        }
    }
}
