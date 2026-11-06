using System.Collections;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Context;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Like the engine (OpenGothic Npc::checkHealth), a human whose hit points are gone goes down unconscious unless
    /// the attacker hates him. NPCs run ZS_Unconscious, the hero (who runs no AI states) lies down and gets up again
    /// after a while.
    /// </summary>
    public class NpcKnockoutDomain
    {
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly PhysicsService _physicsService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly ConfigService _configService;

        // Bumped by every knockout and every load - a recovery of an older one doesn't stand the hero up.
        private int _heroKnockoutId;


        /// <summary>
        /// The target's hit points are gone: true if he is knocked out, false if he dies. Only true humans go down
        /// unconscious - monsters and orcs die. A human is killed if the attacker hates him (permanent attitude
        /// hostile). NPC archers always kill.
        /// </summary>
        public bool IsKnockout(NpcContainer attacker, NpcContainer target)
        {
            if (!target.IsHuman())
                return false;

            var isHeroAttacker = attacker.Instance.IsHero(_gameStateService.GothicVm);
            var attackerWeapon = (VmGothicEnums.WeaponState)attacker.Vob.FightMode;
            if (!isHeroAttacker && attackerWeapon is VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow)
                return false;

            var attitude = _npcAiService.ExtGetPermAttitude(target.Instance, attacker.Instance);
            Logger.Log($"[FightService] {target.Instance.GetName(NpcNameSlot.Slot0)} down by " +
                       $"{attacker.Instance.GetName(NpcNameSlot.Slot0)} - attitude {attitude}", LogCat.Fight);
            return attitude != VmGothicEnums.Attitude.Hostile;
        }

        /// <summary>
        /// Hits right after the knockout don't finish the NPC off.
        /// </summary>
        public bool IsInKnockoutGraceTime(NpcContainer npc)
        {
            return Time.time < npc.Props.KnockedOutTime + FightConst.KnockoutGraceSeconds;
        }

        public void KnockOutNpc(NpcContainer npc, NpcContainer attacker)
        {
            _npcService.SetAttribute(npc, NpcAttribute.HitPoints, 1);

            var vm = _gameStateService.GothicVm;
            var zsUnconscious = vm.GetSymbolByName(DaedalusConst.ZsUnconscious);

            if (zsUnconscious == null)
            {
                Logger.LogWarning("[FightService] ZS_UNCONSCIOUS symbol not found — falling back to S_Wounded anim",
                    LogCat.Fight);
                npc.Props.KnockedOutTime = Time.time;
                npc.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;
                npc.Props.AnimationQueue.Clear();
                npc.PrefabProps.AnimationSystem.StopAllAnimations();
                _physicsService.DisablePhysicsForNpc(npc.PrefabProps);
                var animName = _animationService.GetAnimationName(VmGothicEnums.AnimationType.UnconsciousA, npc);
                npc.PrefabProps.AnimationSystem.PlayAnimation(animName);
                return;
            }

            npc.Props.KnockedOutTime = Time.time;
            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = npc.Instance;
            vm.GlobalOther = attacker.Instance;
            try
            {
                // ZS_Unconscious pays the victory (B_UnconciousXP: only the first one over a human) and then sets
                // AIV_WASDEFEATEDBYSC. StartState is queued, so both happen here at once - a perception before the next
                // frame would see AIV=0 and take the wrong branch.
                var aivWasDefeated = vm.GetSymbolByName("AIV_WASDEFEATEDBYSC")?.GetInt(0) ?? -1;
                var isHeroVictory = attacker.Instance.IsHero(vm) || IsPartyMember(attacker);
                // The temporary attitude goes back to the permanent one, so PERC_ASSESSENEMY doesn't restart the fight
                // with a stale HOSTILE after he wakes up.
                if (isHeroVictory)
                    npc.Vob.AttitudeTemp = npc.Vob.Attitude;
                if (aivWasDefeated >= 0 && isHeroVictory)
                {
                    var bUnconciousXp = vm.GetSymbolByName("B_UNCONCIOUSXP");
                    if (bUnconciousXp != null)
                    {
                        vm.Call(bUnconciousXp.Index);
                        _npcService.SyncHeroInstanceToVob();
                        Logger.Log($"[FightService] Unconscious XP for {npc.Instance.GetName(NpcNameSlot.Slot0)}",
                            LogCat.Npc);
                    }
                    npc.Instance.SetAiVar(aivWasDefeated, 1);
                }

                // ClearState(false) inside ExtAiStartState resets BodyState=BsStand — set BsUnconscious after.
                // Not a script call: the old state's _End (e.g. ZS_ProclaimAndPunish_End) ran on the downed NPC and
                // queued weapon draws and new states - ZS_Unconscious never started and he reacted to everything.
                _npcAiService.ExtAiStartState(npc.Instance, zsUnconscious.Index, true, "", false);
            }
            catch (System.Exception e)
            {
                Logger.LogError($"[FightService] Knockout scripts of {npc.Instance.GetName(NpcNameSlot.Slot0)} " +
                                $"failed: {e}", LogCat.Fight);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
            }
            npc.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;

            // The engine drops the NPC itself - ZS_Unconscious only calls Mdl_ApplyRandomAni for a third of the tough
            // guys, everybody else stood or T-posed while unconscious (Buster) and still stood up normally afterwards.
            if (npc.PrefabProps != null)
            {
                _physicsService.DisablePhysicsForNpc(npc.PrefabProps);
                npc.Props.AnimationQueue.Enqueue(
                    new PlayAni(new AnimationAction(string0: DaedalusConst.AniStandToWounded), npc));
                npc.Props.AnimationQueue.Enqueue(
                    new PlayAni(new AnimationAction(string0: DaedalusConst.AniWounded), npc));
            }

            // Summons fighting the NPC stop like the scripts (ZS_MM_Attack_Loop ends on C_NpcIsDown) - their queued
            // attacks kept hitting the downed NPC.
            foreach (var summon in _multiTypeCacheService.NpcCache)
            {
                if (summon?.SummonedBy == null || summon.Props?.TargetNpc?.Index != npc.Instance.Index)
                    continue;
                _npcAiService.ExtNpcClearAiQueue(summon.Instance);
                Logger.Log($"[FightService] {summon.Instance.GetName(NpcNameSlot.Slot0)} (summon) stops attacking " +
                           $"downed {npc.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
            }

            _npcAiService.BroadcastDownPerception(npc, attacker, VmGothicEnums.PerceptionType.AssessDefeat);
        }

        /// <summary>
        /// What a knocked out or dead hero can't do: walk away and grab. Turning on the spot stays free - seated
        /// players couldn't look around otherwise.
        /// </summary>
        public PlayerControls GetDownLockedControls()
        {
            return PlayerControls.Walking | PlayerControls.Grab;
        }

        public void KnockOutHero(NpcContainer hero, NpcContainer attacker)
        {
            hero.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;
            SetHasDefeatedHero(attacker);
            _npcService.SetAttribute(hero, NpcAttribute.HitPoints, 1);
            _contextInteractionService.LockPlayer(PlayerLocks.Knockout, GetDownLockedControls());

            // Anyone still actively confronting the hero (e.g. mid ZS_ProclaimAndPunish, cut short by
            // this very knockout before its SVM warning/ZS_Attack ever ran) gets its tempAttitude reset
            // to permanent now, the same way KnockOutNpc already does for the reverse case. Without
            // this, vanilla's ZS_ProclaimAndPunish_End silently aborts on C_NpcIsDown(hero) with no
            // attitude reset, so the NPC stays Hostile with no memory that anything happened — once the
            // hero wakes and is re-detected, it replays the whole warning and attacks again ("double
            // beating" for one provocation). The intent here is for the confrontation to be considered
            // over, not merely to skip the SVM line and fight anyway — resetting attitude means the NPC
            // goes back to routine and won't re-engage on its own at all.
            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                if (candidate.Props?.TargetNpc?.Index != hero.Instance.Index)
                    continue;

                candidate.Vob.AttitudeTemp = candidate.Vob.Attitude;

                // Like the engine, the fight ends the moment the hero is down. The NPC's running chase/attack kept
                // its queue busy for seconds, so ZS_Attack_Loop never saw C_NpcIsDown(hero) - a citizen ran in
                // circles and attacked again once the hero stood up. Cleared, the loop ends the fight right away.
                _npcAiService.ExtNpcClearAiQueue(candidate.Instance);

                // The encounter is over, but nothing else tells this NPC to sheath whatever weapon it
                // drew for the confrontation (B_DrawWeapon back in ZS_AssessEnemy) — without this it
                // keeps walking/standing with the weapon out and the wrong animation set even after it
                // gives up and returns to routine.
                if ((VmGothicEnums.WeaponState)candidate.Vob.FightMode != VmGothicEnums.WeaponState.NoWeapon)
                    _npcAiService.ExtAiUndrawWeapon(candidate.Instance);
            }

            _npcAiService.BroadcastDownPerception(hero, attacker, VmGothicEnums.PerceptionType.AssessDefeat);
            GlobalEventDispatcher.HeroKnockedOut.Invoke(hero);

            _unityMonoService.StartCoroutine(KnockoutRecovery(hero, ++_heroKnockoutId));
        }

        /// <summary>
        /// A loaded game ends the hero's knockout: no recovery of the old game stands him up later.
        /// </summary>
        public void CancelHeroRecovery()
        {
            _heroKnockoutId++;
        }

        private bool IsPartyMember(NpcContainer npc)
        {
            var sym = _gameStateService.GothicVm.GetSymbolByName("AIV_MM_PARTYMEMBER");
            if (sym == null) return false;
            var aivIndex = sym.GetInt(0);
            return npc.Instance.GetAiVar(aivIndex) == 1;
        }

        /// <summary>
        /// G1's ZS_Unconscious ends with "if (Npc_IsPlayer(self))
        /// other.aivar[AIV_HASDEFEATEDSC] = TRUE" - the hero runs no Daedalus states, so the NPC who knocked him out
        /// never knew it won. Dialogs after a lost duel check it (Mroczne Tajemnice's Sheer'Ghar: Info_Sheer_After).
        /// </summary>
        private void SetHasDefeatedHero(NpcContainer attacker)
        {
            if (attacker?.Instance == null || attacker.Instance.IsHero(_gameStateService.GothicVm))
                return;

            var aivSymbol = _gameStateService.GothicVm.GetSymbolByName("AIV_HASDEFEATEDSC");
            if (aivSymbol == null)
                return;

            attacker.Instance.SetAiVar(aivSymbol.GetInt(0), 1);
            Logger.Log($"[FightService] {attacker.Instance.GetName(NpcNameSlot.Slot0)} defeated the hero - " +
                       $"AIV_HASDEFEATEDSC ({aivSymbol.GetInt(0)}) = TRUE", LogCat.Fight);
        }

        private IEnumerator KnockoutRecovery(NpcContainer hero, int knockoutId)
        {
            yield return new WaitForSeconds(FightConst.HeroKnockoutRecoverySeconds);
            // A game was loaded meanwhile, or he was knocked out again - that knockout's recovery stands him up.
            if (knockoutId != _heroKnockoutId)
                yield break;
            // Finished off in the meantime - the dead don't stand up.
            if (hero.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                yield break;
            hero.Props.BodyState = VmGothicEnums.BodyState.BsStand;
            _contextInteractionService.UnlockPlayer(PlayerLocks.Knockout);
            Logger.Log("[FightService] Hero recovered from knockout", LogCat.Fight);
            GlobalEventDispatcher.HeroRecovered.Invoke();
        }
    }
}
