using System.Collections;
using Gothic.Core.Domain.Npc;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Context;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
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
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        private readonly NpcDeathDomain _deathDomain = new NpcDeathDomain().Inject();

        // Bumped by every knockout and every load - a recovery of an older one doesn't stand the hero up.
        private int _heroKnockoutId;

        public void Init()
        {
            // Scripts kill (B_KillNpc) with or without the combat system.
            GlobalEventDispatcher.ScriptHitPointsDepleted.AddListener(OnScriptHitPointsDepleted);
            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);

            if (!_configService.Dev.EnableCombatSystem)
                return;

            GlobalEventDispatcher.FightHit.AddListener(OnHit);
        }

        /// <summary>
        /// Like the engine (OpenGothic Npc::changeAttribute -> checkHealth), a script taking the NPC's last hit points
        /// kills it. The NPC is its own killer - nobody gets experience from ZS_Dead: B_MagicHurtNpc pays the caster
        /// itself, B_KillNpc (B_ClearFMC) and fading summons pay nothing. The scripts' `other` is whoever the running
        /// state had (often the hero).
        /// </summary>
        private void OnScriptHitPointsDepleted(NpcContainer victim)
        {
            // The hero too: like the engine, a script taking his last hit points kills him (no knockout).
            if (victim?.Props == null || victim.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                return;

            // Not built yet (the scripts' startup kills corpses) or culled: only the dead state - running ZS_Dead in the
            // middle of the startup's VM call hung the loading. A culled NPC comes back lying dead (AiHandler).
            // The VR hero has no animated body, but is always there: his death ends the game (drowning).
            var isHero = victim.Instance.IsHero(_gameStateService.GothicVm);
            if (!isHero && (victim.Go == null || victim.PrefabProps?.AnimationSystem == null))
            {
                _deathDomain.Die(victim);
                return;
            }

            Logger.Log($"[FightService] {victim.Instance.GetName(NpcNameSlot.Slot0)} lost its last hit points by " +
                       "script - dies", LogCat.Fight);
            Kill(victim, victim);
        }

        private void OnHit(NpcContainer attacker, NpcContainer target, Vector3 __)
        {
            if (target.Props.IsDead)
                return;

            // The two in a dialog can't hurt each other - others can still hit them (the engine has no dialog check).
            if (IsInCurrentDialog(attacker) || IsInCurrentDialog(target))
            {
                Logger.Log("[FightService] Hit blocked — dialog in progress", LogCat.Fight);
                return;
            }

            var isHero = target.PrefabProps != null && target.PrefabProps.IsHero();

            // Ignore hits on already-unconscious hero — prevents stacking recovery coroutines.
            if (isHero && target.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return;

            // Any attacker finishes off unconscious NPC — set dead and call ZS_Dead. Not the swing that knocked him out:
            // a VR weapon can enter several hitboxes in one swing - Bullit died instead of lying down.
            if (target.Props.IsKnockedOut && !isHero)
            {
                if (Time.time < target.Props.KnockedOutTime + _knockoutGraceSeconds)
                    return;

                Logger.Log($"[FightService] {attacker.Instance.GetName(NpcNameSlot.Slot0)} finishes off unconscious {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
                Kill(target, attacker);
                return;
            }

            Logger.Log($"[FightService.OnHit] *** {attacker.Instance.GetName(NpcNameSlot.Slot0)} HIT {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
            if (OnHitUpdateHealth(attacker, target))
            {
                // The hero goes down by the same rule as everybody (OpenGothic checkHealth): monsters and humans
                // hostile to him kill him, the others knock him out.
                var isKnockedOut = IsHuman(target) && IsKnockedOutNotKilled(attacker, target);
                if (isHero && isKnockedOut)
                {
                    Logger.LogWarning("[FightService] Hero knocked out!", LogCat.Fight);
                    OnHeroKnockedOut(target, attacker);
                }
                else if (isKnockedOut)
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is UNCONSCIOUS", LogCat.Npc);
                    OnNpcKnockedOut(target, attacker);
                }
                else
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is DEAD", LogCat.Npc);
                    Kill(target, attacker);
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

        /// <summary>
        /// Like the engine: the victim gets PERC_ASSESSDAMAGE, NPCs around the attacker that see him
        /// PERC_ASSESSOTHERSDAMAGE / PERC_ASSESSFIGHTSOUND (monsters in routine states react through
        /// B_MM_ReactToOthersDamage). Who becomes whose target is the scripts' and the fight AI's business.
        /// </summary>
        private void BroadcastDamagePerceptions(NpcContainer attacker, NpcContainer target)
        {
            // Skip on a target that just died from this hit: its registered AssessDamage handler
            // (e.g. ZS_MM_Attack, shared by monsters/summons) unconditionally calls AI_StandUp,
            // which resets BodyState back to BsStand and undoes the death we just set above.
            if (target.Props.BodyState != VmGothicEnums.BodyState.BsDead)
            {
                // Like the engine's stumble, a hit interrupts walking - the reaction (ZS_ReactToDamage:
                // AI_StartState(ZS_AssessMonster)) queued behind a guide's long AI_GotoWP and never ran: Baal Parvez stood
                // still under a wolf's bites until spoken to.
                if (!target.PrefabProps.IsHero() &&
                    target.Props.CurrentAction is AbstractWalkAnimationAction or AbstractWalkAnimationAction2)
                {
                    Logger.Log($"[FightService] {target.Instance.GetName(NpcNameSlot.Slot0)} stops walking - hit by " +
                               $"{attacker.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
                    _npcAiService.ExtNpcClearAiQueue(target.Instance);
                }

                _npcAiService.ExecutePerception(
                    VmGothicEnums.PerceptionType.AssessDamage,
                    target.Props, target.Instance,
                    victim: target.Instance,
                    other: attacker.Instance);
            }

            if (attacker.Go == null) return;

            var broadcasted = 0;
            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                if (candidate == target || candidate == attacker)
                    continue;
                if (candidate.Props?.IsDown == true)
                    continue;
                // Skip culled NPCs — Go is null or inactive (out of cull range).
                if (candidate.Go == null || !candidate.Go.activeInHierarchy)
                    continue;

                var sensesRangeM = candidate.Instance.SensesRange / 100f;
                var dist = Vector3.Distance(candidate.Go.transform.position, attacker.Go.transform.position);
                if (dist > sensesRangeM)
                    continue;

                // Require visual LOS so NPCs behind walls don't charge through geometry.
                // freeLOS=true: skip FOV check — bystanders can react from any angle as long as there's no wall.
                if (!_npcAiService.ExtNpcCanSeeNpc(candidate.Instance, attacker.Instance, true))
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
                else if (candidate.Instance.Guild >= (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM &&
                         candidate.Instance.Guild < GetOrcSeparatorGuild())
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

        // Hits right after the knockout don't finish the NPC off.
        private const float _knockoutGraceSeconds = 1f;

        // The scripts' GIL_SEPERATOR_ORC: monsters are below it, orcs above (G1 37, G2 58).
        private int? _orcSeparatorGuild;

        private int GetOrcSeparatorGuild()
        {
            _orcSeparatorGuild ??= _gameStateService.GothicVm.GetSymbolByName("GIL_SEPERATOR_ORC")?.GetInt(0) ??
                                   (int)VmGothicEnums.Guild.GIL_SEPERATOR_ORC;
            return _orcSeparatorGuild.Value;
        }

        // Only true humans (guild < GIL_SEPERATOR_HUM, G1 and G2) go unconscious - monsters and orcs die.
        private static bool IsHuman(NpcContainer npc) =>
            npc.Instance.Guild < (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM;

        private bool IsPartyMember(NpcContainer npc)
        {
            var sym = _gameStateService.GothicVm.GetSymbolByName("AIV_MM_PARTYMEMBER");
            if (sym == null) return false;
            var aivIndex = sym.GetInt(0);
            return npc.Instance.GetAiVar(aivIndex) == 1;
        }

        /// <summary>
        /// Like the engine (OpenGothic Npc::checkHealth): a human goes down unconscious unless the attacker hates him
        /// (permanent attitude hostile) - then he's killed. NPC archers always kill.
        /// </summary>
        private bool IsKnockedOutNotKilled(NpcContainer attacker, NpcContainer target)
        {
            var isHeroAttacker = attacker.PrefabProps != null && attacker.PrefabProps.IsHero();
            var attackerWeapon = (VmGothicEnums.WeaponState)attacker.Vob.FightMode;
            if (!isHeroAttacker && attackerWeapon is VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow)
                return false;

            var attitude = _npcAiService.ExtGetPermAttitude(target.Instance, attacker.Instance);
            Logger.Log($"[FightService] {target.Instance.GetName(NpcNameSlot.Slot0)} down by " +
                       $"{attacker.Instance.GetName(NpcNameSlot.Slot0)} - attitude {attitude}", LogCat.Fight);
            return attitude != VmGothicEnums.Attitude.Hostile;
        }

        private void OnNpcKnockedOut(NpcContainer npc, NpcContainer attacker)
        {
            _npcService.SetAttribute(npc, NpcAttribute.HitPoints, 1);

            var vm = _gameStateService.GothicVm;
            var zsUnconscious = vm.GetSymbolByName("ZS_UNCONSCIOUS");

            if (zsUnconscious == null)
            {
                Logger.LogWarning("[FightService] ZS_UNCONSCIOUS symbol not found — falling back to S_Wounded anim", LogCat.Fight);
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
                var isHeroVictory = attacker.PrefabProps.IsHero() || IsPartyMember(attacker);
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
                        Logger.Log($"[FightService] Unconscious XP for {npc.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
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
                Logger.LogError($"[FightService] Knockout scripts of {npc.Instance.GetName(NpcNameSlot.Slot0)} failed: {e}",
                    LogCat.Fight);
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
                npc.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(string0: "T_Stand_2_Wounded"), npc));
                npc.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(string0: "S_Wounded"), npc));
            }

            BroadcastDownPerception(npc, attacker, VmGothicEnums.PerceptionType.AssessDefeat);
        }

        /// <summary>
        /// What a knocked out or dead hero can't do: walk away and grab. DeveloperConfig.EnableKnockoutTurning keeps
        /// turning on the spot free.
        /// </summary>
        public PlayerControls GetDownLockedControls()
        {
            return _configService.Dev.EnableKnockoutTurning
                ? PlayerControls.Walking | PlayerControls.Grab
                : PlayerControls.All;
        }

        private void OnHeroKnockedOut(NpcContainer hero, NpcContainer attacker)
        {
            hero.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;
            SetHasDefeatedHero(attacker);
            _npcService.SetAttribute(hero, NpcAttribute.HitPoints, 1);
            _contextInteractionService.LockPlayer(PlayerLocks.Knockout, GetDownLockedControls());

            // Anyone still actively confronting the hero (e.g. mid ZS_ProclaimAndPunish, cut short by
            // this very knockout before its SVM warning/ZS_Attack ever ran) gets its tempAttitude reset
            // to permanent now, the same way OnNpcKnockedOut already does for the reverse case. Without
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

            BroadcastDownPerception(hero, attacker, VmGothicEnums.PerceptionType.AssessDefeat);
            GlobalEventDispatcher.HeroKnockedOut.Invoke(hero);

            _unityMonoService.StartCoroutine(KnockoutRecovery(hero, ++_heroKnockoutId));
        }

        /// <summary>
        /// G1's ZS_Unconscious ends with "if (Npc_IsPlayer(self))
        /// other.aivar[AIV_HASDEFEATEDSC] = TRUE" - the hero runs no Daedalus states, so the NPC who knocked him out
        /// never knew it won. Dialogs after a lost duel check it (Mroczne Tajemnice's Sheer'Ghar: Info_Sheer_After).
        /// </summary>
        private void SetHasDefeatedHero(NpcContainer attacker)
        {
            if (attacker?.Instance == null ||
                (attacker.PrefabProps != null && attacker.PrefabProps.IsHero()))
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
            yield return new WaitForSeconds(10f);
            // A game was loaded meanwhile, or he was knocked out again - that knockout's recovery stands him up.
            if (knockoutId != _heroKnockoutId)
                yield break;
            // Finished off in the meantime - the dead don't stand up.
            if (hero.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                yield break;
            hero.Props.BodyState = VmGothicEnums.BodyState.BsStand;
            _contextInteractionService.UnlockPlayer(PlayerLocks.Knockout);
            Logger.Log("[FightService] Hero recovered from knockout", LogCat.Fight);
        }

        /// <summary>
        /// A loaded game ends the hero's knockout and death: no recovery of the old game stands him up later.
        /// </summary>
        private void OnLoadGameStart()
        {
            _heroKnockoutId++;
            _contextInteractionService.UnlockPlayer(PlayerLocks.Knockout | PlayerLocks.Death);
        }

        private bool IsInCurrentDialog(NpcContainer npc)
        {
            var dialogs = _gameStateService.Dialogs;
            if (!dialogs.IsInDialog || npc == null)
                return false;

            return npc == dialogs.CurrentDialogNpc || (npc.PrefabProps != null && npc.PrefabProps.IsHero());
        }

        private int? _minimalDamage;

        private int GetMinimalDamage()
        {
            _minimalDamage ??= _gameStateService.GothicVm.GetSymbolByName("NPC_MINIMAL_DAMAGE")?.GetInt(0) ?? 0;
            return _minimalDamage.Value;
        }

        /// <summary>
        /// Handles health changes. Returns true if the hit points are gone - OnHit decides: knocked out or dead.
        /// </summary>
        private bool OnHitUpdateHealth(NpcContainer attacker, NpcContainer target)
        {
            var hitPoints = target.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            var maxHP = target.Vob.GetAttribute((int)NpcAttribute.HitPointsMax);

            var damage = CalculateMeleeDamage(attacker, target, out var isImmune);

            // Like the engine, a hit never does less than NPC_MINIMAL_DAMAGE (Balancing.d, G1: 1) unless the target is
            // immune (-1).
            if (!isImmune)
                damage = Mathf.Max(damage, GetMinimalDamage());

            // NPC_FLAG_IMMORTAL (bit 1 = 2): take no damage, but combat still plays out normally.
            if (((int)target.Instance.Flags & 2) != 0)
            {
                Logger.Log($"[FightService] {target.Instance.GetName(NpcNameSlot.Slot0)} is immortal — 0 damage", LogCat.Npc);
                damage = 0;
            }

            Logger.Log($"[FightService.OnHitUpdateHealth] {target.Instance.GetName(NpcNameSlot.Slot0)}: {hitPoints} - {damage} dmg", LogCat.Npc);

            hitPoints -= damage;

            Logger.Log($"[FightService.OnHitUpdateHealth] {target.Instance.GetName(NpcNameSlot.Slot0)} HP after: {hitPoints}/{maxHP}", LogCat.Npc);

            // Health bars follow via GlobalEventDispatcher.NpcAttributeChanged.
            _npcService.SetAttribute(target, NpcAttribute.HitPoints, hitPoints);

            return hitPoints <= 0;
        }

        private void OnNpcDied(NpcContainer dead, NpcContainer killer)
        {
            // Dead is 0 hit points like the engine (OpenGothic Npc::finishingMove / C_Kill: hit points -max) - a
            // finished off NPC kept the knockout's 1 HP.
            if (dead.Vob.GetAttribute((int)NpcAttribute.HitPoints) > 0)
            {
                _npcService.SetAttribute(dead, NpcAttribute.HitPoints, 0);
            }

            // ZS_Dead (B_CheckDeadMissionNPCs + B_GiveDeathInv etc.) takes ~280ms in G2 — too heavy
            // to run synchronously in the hit frame. Defer by one frame so the frame spike is gone.
            var deadInstance = dead.Instance;
            _unityMonoService.StartCoroutine(CallZsDeadDeferred(deadInstance, killer));

            Logger.Log($"[FightService.OnNpcDied] {dead.Instance.GetName(NpcNameSlot.Slot0)} died — ZS_Dead deferred (killer: {killer.Instance.GetName(NpcNameSlot.Slot0)})", LogCat.Npc);
        }

        private IEnumerator CallZsDeadDeferred(NpcInstance dead, NpcContainer killer)
        {
            yield return null;

            var vm = _gameStateService.GothicVm;
            var zsDeadSym = vm.GetSymbolByName("ZS_Dead");
            if (zsDeadSym == null)
            {
                Logger.LogWarning("[FightService.OnNpcDied] ZS_Dead symbol not found", LogCat.Npc);
                yield break;
            }

            // Like the engine's state change: the state he died in is the last one - G1's B_DeathXP pays nothing for
            // finishing off an unconscious human (Npc_WasInState(self, ZS_Unconscious)).
            var deadContainer = dead.GetUserData();
            if (deadContainer?.Vob != null)
            {
                deadContainer.Vob.LastAiState = deadContainer.Vob.CurrentStateIndex;
                deadContainer.Vob.CurrentStateIndex = zsDeadSym.Index;
                deadContainer.Vob.CurrentStateName = zsDeadSym.Name;
            }

            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = dead;
            // Like the engine (OpenGothic checkHealth: currentOther) 'other' is the killer - ZS_Dead gives XP only if he's
            // the hero or in his party (AIV_PARTYMEMBER, AIV_MM_PARTYMEMBER: the hero's summons, Mordrag leading him),
            // and G2 marks only the hero's victims as killed by him. The hero for everybody gave XP for every death.
            vm.GlobalOther = killer?.Instance ?? dead;
            try
            {
                vm.Call(zsDeadSym.Index);

                // The engine runs ZS_Dead_Loop too - G2 counts killed dragons there (B_DragonKillCounter): the undead
                // dragon's EXTRO video, the teleport to UNDEAD_ENDTELEPORT and UndeadDragonIsDead never happened. The
                // scripts guard it themselves (AIV_TAPOSITION), once is enough.
                var zsDeadLoopSym = vm.GetSymbolByName("ZS_Dead_Loop");
                if (zsDeadLoopSym != null)
                {
                    if (zsDeadLoopSym.HasReturn)
                        vm.Call<int>(zsDeadLoopSym.Index);
                    else
                        vm.Call(zsDeadLoopSym.Index);
                }

                _npcService.SyncHeroInstanceToVob();
                Logger.Log($"[FightService.OnNpcDied] ZS_Dead complete: {dead.GetName(NpcNameSlot.Slot0)}" +
                           $"{(zsDeadLoopSym != null ? " (+ ZS_Dead_Loop)" : "")}", LogCat.Npc);
            }
            catch (System.Exception e)
            {
                Logger.LogError($"[FightService.OnNpcDied] ZS_Dead of {dead.GetName(NpcNameSlot.Slot0)} failed: " +
                                e.Message, LogCat.Npc);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
            }

            BroadcastDownPerception(deadContainer, killer, VmGothicEnums.PerceptionType.AssessMurder);
        }

        /// <summary>
        /// Like the engine, witnesses around the victim learn who
        /// killed (PERC_ASSESSMURDER) or knocked out (PERC_ASSESSDEFEAT) whom: other = killer, victim = victim.
        /// The killer doesn't witness its own deed.
        /// </summary>
        private void BroadcastDownPerception(NpcContainer victim, NpcContainer killer, VmGothicEnums.PerceptionType perception)
        {
            if (victim == null || killer == null || victim == killer)
                return;

            if (_npcAiService.BroadcastPassivePerception(victim, perception, victim.Instance, killer.Instance,
                    exclude: killer))
                Logger.Log($"[FightService.Perc] {perception}: {killer.Instance.GetName(NpcNameSlot.Slot0)} -> " +
                           $"{victim.Instance.GetName(NpcNameSlot.Slot0)} sent", LogCat.Fight);
        }

        /// <summary>
        /// Like the engine (OpenGothic DamageCalculator::swordDamage). Per damage type of the weapon (or the NPC's own
        /// damagetype: fists, claws - wolves bite DAM_EDGE): strength + the NPC's damage[] + the weapon's damage[] and
        /// damageTotal - the target's protection of that type; immune types (protection -1) are left out.
        /// G1: a critical hit with the talent value's chance (1H/2H) doubles the weapon part (DAM_CRITICAL_MULTIPLIER).
        /// G2: a miss by the hit chance (C_NPC.hitchance[1H/2H]) does a tenth; monsters without a weapon always hit.
        /// </summary>
        private int CalculateMeleeDamage(NpcContainer attacker, NpcContainer target, out bool isImmune)
        {
            var weapon = _npcHelperService.ExtNpcGetEquippedMeleeWeapon(attacker.Instance);
            if ((VmGothicEnums.WeaponState)attacker.Vob.FightMode == VmGothicEnums.WeaponState.Fist)
                weapon = null;

            var mask = weapon?.DamageType ?? attacker.Instance.DamageType;
            if (mask == 0)
                mask = 1 << (int)DamageType.Blunt;

            var vm = _gameStateService.GothicVm;
            var isTwoHanded = weapon != null && ((VmGothicEnums.ItemFlags)weapon.Flags &
                (VmGothicEnums.ItemFlags.Item2HdSwd | VmGothicEnums.ItemFlags.Item2HdAxe)) != 0;
            var roll = Random.Range(0, 100);
            var isG2 = _contextGameVersionService.IsGothic2();
            bool isCritical;
            if (isG2)
            {
                // G2: the "critical" roll is the hit chance - below it the hit is a near miss.
                isCritical = (!IsHuman(attacker) && weapon == null) ||
                             attacker.Instance.GetHitChance(isTwoHanded ? NpcTalent.TwoHanded : NpcTalent.OneHanded) > roll;
            }
            else
            {
                var talentId = vm.GetSymbolByName(isTwoHanded ? "NPC_TALENT_2H" : "NPC_TALENT_1H")?.GetInt(0) ??
                               (isTwoHanded ? 2 : 1);
                isCritical = weapon != null && GetG1CriticalChance(attacker, talentId) > roll;
            }
            var multiplier = vm.GetSymbolByName("DAM_CRITICAL_MULTIPLIER")?.GetInt(0) ?? 2;
            var strength = attacker.Vob.GetAttribute((int)NpcAttribute.Strength);

            var damage = 0;
            isImmune = true;
            for (var i = 0; i < 8; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;

                var protection = target.Vob.GetProtection(i);
                if (protection < 0)
                    continue;

                var typeDamage = attacker.Instance.GetDamage((DamageType)i) +
                                 (weapon != null ? weapon.GetDamage((DamageType)i) + weapon.DamageTotal : 0);
                int value;
                if (isG2)
                {
                    value = Mathf.Max(strength + typeDamage - protection, 0);
                    if (!isCritical)
                        value = (value - 1) / 10;
                }
                else
                {
                    value = Mathf.Max(strength + (isCritical ? multiplier * typeDamage : typeDamage) - protection, 0);
                }
                damage += value;
                isImmune = false;
            }

            var rollResult = isG2 ? (isCritical ? "hit" : "near miss") : (isCritical ? "CRITICAL" : "normal");
            Logger.Log($"[FightService.MeleeDamage] {attacker.Instance.GetName(NpcNameSlot.Slot0)} -> " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)}: weapon '{weapon?.Name ?? "none"}' mask {mask}, " +
                       $"strength {strength}, {rollResult} (roll {roll}) = {damage}{(isImmune ? " (immune)" : "")}",
                LogCat.Fight);
            return damage;
        }

        // Mroczne Tajemnice's teachers (B_GiveprocentSkill) keep a weapon's percent in another talent's value - the one
        // its status menu shows as the critical hit chance.
        private const string _modPercentSkillFunction = "B_GIVEPROCENTSKILL";

        private static readonly (string Weapon, string Percent)[] _modPercentTalents =
        {
            ("NPC_TALENT_1H", "NPC_TALENT_SNEAK"),
            ("NPC_TALENT_2H", "NPC_TALENT_REGENERATE"),
            ("NPC_TALENT_BOW", "NPC_TALENT_FIREMASTER"),
            ("NPC_TALENT_CROSSBOW", "NPC_TALENT_ACROBAT")
        };

        /// <summary>
        /// G1's critical hit chance: the weapon talent's value. With Mroczne Tajemnice's percent system the talent its teachers raise counts too (the higher one) - a hero trained by
        /// Cavalorn had his bow percent in NPC_TALENT_FIREMASTER and never hit critically.
        /// </summary>
        private int GetG1CriticalChance(NpcContainer npc, int talentId)
        {
            var chance = _npcHelperService.ExtNpcGetTalentValue(npc.Instance, talentId);
            var vm = _gameStateService.GothicVm;
            if (vm?.GetSymbolByName(_modPercentSkillFunction) == null)
                return chance;

            foreach (var (weapon, percent) in _modPercentTalents)
            {
                if (vm.GetSymbolByName(weapon)?.GetInt(0) != talentId)
                    continue;
                var percentTalent = vm.GetSymbolByName(percent);
                if (percentTalent != null)
                    chance = Mathf.Max(chance,
                        _npcHelperService.ExtNpcGetTalentValue(npc.Instance, percentTalent.GetInt(0)));
                break;
            }
            return chance;
        }

        /// <summary>
        /// The NPC dies: dead state, AI queue dropped, the dying animation (NpcDeathDomain), then ZS_Dead with the killer.
        /// </summary>
        private void Kill(NpcContainer victim, NpcContainer killer)
        {
            _deathDomain.Die(victim);
            OnNpcDied(victim, killer);

            if (victim.Instance.IsHero(_gameStateService.GothicVm))
                EndGame(victim, killer);
        }

        /// <summary>
        /// The hero's death ends the game like in Gothic: he can't move, teleport or grab any more (until a game is
        /// loaded), and the menu offers loading, but no saving or continuing (InGameAndAlive).
        /// </summary>
        private void EndGame(NpcContainer hero, NpcContainer killer)
        {
            _gameStateService.InGameAndAlive = false;
            _contextInteractionService.LockPlayer(PlayerLocks.Death, GetDownLockedControls());
            PlayHeroDeathScream(hero);

            var killerName = killer == null || killer == hero ? "a script" : killer.Instance.GetName(NpcNameSlot.Slot0);
            Logger.Log($"[FightService] The hero was killed by {killerName}. Game over.", LogCat.Fight);
            GlobalEventDispatcher.HeroDied.Invoke(killer);
        }

        /// <summary>
        /// The VR hero has no body that plays the dying animation - his scream comes from where his lines come from.
        /// </summary>
        private void PlayHeroDeathScream(NpcContainer hero)
        {
            var source = hero.Go != null ? hero.Go.GetComponent<AudioSource>() : null;
            if (hero.Instance.Voice <= 0 || source == null)
                return;

            var clip = _audioService.GetRandomSoundClip($"SVM_{hero.Instance.Voice}_DEAD");
            if (clip != null)
                source.PlayOneShot(clip);
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

            // The hero's pain sound like the NPCs' - from his own voice source (he has no NpcSound, nothing was heard).
            if (target.PrefabProps != null && target.PrefabProps.IsHero())
            {
                var heroGo = _npcService.GetHeroGameObject();
                var heroSource = heroGo != null ? heroGo.GetComponent<AudioSource>() : null;
                if (heroSource != null)
                {
                    heroSource.PlayOneShot(clip);
                    return;
                }
            }

            if (target.PrefabProps == null || target.PrefabProps.NpcSound == null)
                return;
            target.PrefabProps.NpcSound.PlayOneShot(clip);
        }
    }
}
