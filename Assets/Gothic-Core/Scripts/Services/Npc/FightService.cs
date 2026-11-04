using Gothic.Core.Domain.Npc;
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
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// The one owner of how an NPC goes down: a melee hit (damage: MeleeDamageDomain), the hit points gone otherwise
    /// (HitPointsDepleted), knockout (NpcKnockoutDomain) or death (NpcDeathDomain), the hero's death ending the game.
    /// </summary>
    public class FightService
    {
        [Inject] private AudioService _audioService;
        [Inject] private AnimationService _animationService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly ConfigService _configService;

        private readonly NpcDeathDomain _deathDomain = new NpcDeathDomain().Inject();
        private readonly NpcKnockoutDomain _knockoutDomain = new NpcKnockoutDomain().Inject();
        private readonly MeleeDamageDomain _meleeDamageDomain = new MeleeDamageDomain().Inject();

        // The scripts' GIL_SEPERATOR_ORC: monsters are below it, orcs above (G1 37, G2 58).
        private int? _orcSeparatorGuild;

        public void Init()
        {
            // Scripts (B_KillNpc), drowning and damage zones kill with or without the combat system.
            GlobalEventDispatcher.HitPointsDepleted.AddListener(OnHitPointsDepleted);
            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);

            if (!_configService.Dev.EnableCombatSystem)
                return;

            GlobalEventDispatcher.FightHit.AddListener(OnHit);
        }

        /// <summary>
        /// Like the engine (OpenGothic Npc::changeAttribute -> checkHealth), losing the last hit points outside of a
        /// melee hit (a script, drowning, a damage zone) kills. The NPC is its own killer - nobody gets experience from
        /// ZS_Dead: B_MagicHurtNpc pays the caster itself, B_KillNpc (B_ClearFMC) and fading summons pay nothing. The
        /// scripts' `other` is whoever the running state had (often the hero).
        /// </summary>
        private void OnHitPointsDepleted(NpcContainer victim)
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

            Logger.Log($"[FightService] {victim.Instance.GetName(NpcNameSlot.Slot0)} lost its last hit points " +
                       "outside of a hit - dies", LogCat.Fight);
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

            var isHero = target.Instance.IsHero(_gameStateService.GothicVm);

            // Ignore hits on already-unconscious hero — prevents stacking recovery coroutines.
            if (isHero && target.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return;

            // Any attacker finishes off unconscious NPC — set dead and call ZS_Dead. Not the swing that knocked him out:
            // a VR weapon can enter several hitboxes in one swing - Bullit died instead of lying down.
            if (target.Props.IsKnockedOut && !isHero)
            {
                if (_knockoutDomain.IsInKnockoutGraceTime(target))
                    return;

                Logger.Log($"[FightService] {attacker.Instance.GetName(NpcNameSlot.Slot0)} finishes off unconscious " +
                           $"{target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
                Kill(target, attacker);
                return;
            }

            Logger.Log($"[FightService.OnHit] *** {attacker.Instance.GetName(NpcNameSlot.Slot0)} HIT " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
            if (_meleeDamageDomain.ApplyHit(attacker, target))
            {
                // The hero goes down by the same rule as everybody (OpenGothic checkHealth): monsters and humans
                // hostile to him kill him, the others knock him out.
                var isKnockedOut = _knockoutDomain.IsKnockout(attacker, target);
                if (isHero && isKnockedOut)
                {
                    Logger.LogWarning("[FightService] Hero knocked out!", LogCat.Fight);
                    _knockoutDomain.KnockOutHero(target, attacker);
                }
                else if (isKnockedOut)
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is UNCONSCIOUS",
                        LogCat.Npc);
                    _knockoutDomain.KnockOutNpc(target, attacker);
                }
                else
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is DEAD",
                        LogCat.Npc);
                    Kill(target, attacker);
                }
            }
            else
            {
                Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} took damage, playing " +
                           "hurt animation", LogCat.Npc);
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
                if (!target.Instance.IsHero(_gameStateService.GothicVm) &&
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

        private int GetOrcSeparatorGuild()
        {
            _orcSeparatorGuild ??= _gameStateService.GothicVm.GetSymbolByName("GIL_SEPERATOR_ORC")?.GetInt(0) ??
                                   (int)VmGothicEnums.Guild.GIL_SEPERATOR_ORC;
            return _orcSeparatorGuild.Value;
        }

        /// <summary>
        /// A loaded game ends the hero's knockout and death: no recovery of the old game stands him up later.
        /// </summary>
        private void OnLoadGameStart()
        {
            _knockoutDomain.CancelHeroRecovery();
            _contextInteractionService.UnlockPlayer(PlayerLocks.Knockout | PlayerLocks.Death);
        }

        private bool IsInCurrentDialog(NpcContainer npc)
        {
            var dialogs = _gameStateService.Dialogs;
            if (!dialogs.IsInDialog || npc == null)
                return false;

            return npc == dialogs.CurrentDialogNpc || npc.Instance.IsHero(_gameStateService.GothicVm);
        }

        /// <summary>
        /// The NPC dies: dead state, AI queue dropped, the dying animation (NpcDeathDomain), then ZS_Dead with the
        /// killer.
        /// </summary>
        private void Kill(NpcContainer victim, NpcContainer killer)
        {
            _deathDomain.Die(victim);
            _deathDomain.StartDeadState(victim, killer);

            if (victim.Instance.IsHero(_gameStateService.GothicVm))
                EndGame(victim, killer);
        }

        /// <summary>
        /// The hero's death ends the game like in Gothic: he can't move, teleport or grab any more (until a game is
        /// loaded), and the menu offers loading, but no saving or continuing (IsHeroAlive).
        /// </summary>
        private void EndGame(NpcContainer hero, NpcContainer killer)
        {
            _gameStateService.HeroDies();
            _contextInteractionService.LockPlayer(PlayerLocks.Death, PlayerControls.Walking | PlayerControls.Grab);
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
            if (target.Instance.IsHero(_gameStateService.GothicVm))
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
