using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Context;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Culling;
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
    /// <summary>
    /// The one owner of how an NPC goes down: a hit (melee damage: MeleeDamageDomain, spells: SpellHitDomain,
    /// projectiles: RangedHitDomain, blocks: HeroBlockDomain), the hit points gone otherwise (HitPointsDepleted),
    /// knockout (NpcKnockoutDomain) or death (NpcDeathDomain), the hero's death ending the game. Who reacts to a hit:
    /// DamagePerceptionDomain.
    /// </summary>
    public class FightService
    {
        [Inject] private AudioService _audioService;
        [Inject] private AnimationService _animationService;
        [Inject] private PhysicsService _physicsService;
        [Inject] private NpcHelperService _npcHelperService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly Gothic.Core.Services.Player.PlayerService _playerService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly INpcJumpFallFactory _npcJumpFallFactory;

        private readonly NpcDeathDomain _deathDomain = new NpcDeathDomain().Inject();
        private readonly NpcKnockoutDomain _knockoutDomain = new NpcKnockoutDomain().Inject();
        private readonly MeleeDamageDomain _meleeDamageDomain = new MeleeDamageDomain().Inject();
        private readonly HeroBlockDomain _heroBlockDomain = new HeroBlockDomain().Inject();
        private readonly DamagePerceptionDomain _damagePerceptionDomain = new DamagePerceptionDomain().Inject();
        private readonly FightRangeDomain _fightRangeDomain = new FightRangeDomain().Inject();
        private readonly SpellHitDomain _spellHitDomain;
        private readonly RangedHitDomain _rangedHitDomain;


        public FightService()
        {
            // Spells and projectiles hit through OnHit with their own damage and protection.
            _spellHitDomain = new SpellHitDomain(ApplyHit).Inject();
            _rangedHitDomain = new RangedHitDomain(ApplyHit).Inject();
        }

        /// <summary>
        /// The engine's preferred attack distance (m) of self against target - see FightRangeDomain.
        /// </summary>
        public float GetPreferredAttackDistance(NpcContainer self, NpcContainer target, bool isMelee = false)
        {
            return _fightRangeDomain.GetPreferredAttackDistance(self, target, isMelee);
        }

        /// <summary>
        /// Horizontal distance (m) compared with GetPreferredAttackDistance().
        /// </summary>
        public float GetFightDistance(NpcContainer self, NpcContainer target)
        {
            return _fightRangeDomain.GetFightDistance(self, target);
        }

        public void Init()
        {
            // Scripts (B_KillNpc), drowning and damage zones kill with or without the combat system.
            GlobalEventDispatcher.HitPointsDepleted.AddListener(OnHitPointsDepleted);
            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);

            if (!_configService.Dev.EnableCombatSystem)
                return;

            GlobalEventDispatcher.FightHit.AddListener(OnHit);
            GlobalEventDispatcher.SpellHit.AddListener(_spellHitDomain.OnSpellHit);
            GlobalEventDispatcher.SpellSpreadHit.AddListener((caster, pos, damage) =>
                _spellHitDomain.ApplySpreadHit(caster, pos, damage, null));
            GlobalEventDispatcher.FallDamage.AddListener(OnFallDamage);
            GlobalEventDispatcher.CheatKill.AddListener(OnCheatKill);
            GlobalEventDispatcher.RangedHit.AddListener(_rangedHitDomain.OnRangedHit);
            GlobalEventDispatcher.FightFinishingMove.AddListener(OnFinishingMove);
            GlobalEventDispatcher.HeroMeleeSwing.AddListener(OnHeroMeleeSwing);
        }

        /// <summary>
        /// An NPC fires its active spell at its target (AttackPlayAni's hit frame) - see SpellHitDomain.CastNpcSpell.
        /// </summary>
        public void CastNpcSpell(NpcContainer caster, NpcContainer target, Vector3 targetPos)
        {
            _spellHitDomain.CastNpcSpell(caster, target, targetPos);
        }

        /// <summary>
        /// The hero's swing is his "prehit" for a moment. NPCs fighting him
        /// stop a wait or strafe (the safe moves to cut short), so their next AI_Attack can parry or jump back - they
        /// pick a move only once per AI_Attack, and a VR swing is over before a waiting NPC would look again.
        /// </summary>
        private void OnHeroMeleeSwing(int weaponMaterial)
        {
            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null)
                return;
            hero.Props.MeleeSwingMaterial = weaponMaterial;

            hero.Props.MeleePrehitUntil = Time.time + FightConst.HeroPrehitSeconds;

            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            foreach (var npc in awakeNpcs)
            {
                if (npc == hero || npc?.Go == null || npc.Props == null || npc.IsDown())
                    continue;
                var enemy = npc.Props.EnemyNpc ?? npc.Props.TargetNpc;
                if (enemy == null || enemy.Index != hero.Instance.Index ||
                    Vector3.Distance(npc.Go.transform.position, hero.Go.transform.position) >
                    FightConst.HeroPrehitRangeMeters)
                    continue;

                // An NPC in its own attack (combos of the tougher ones) never
                // looked at the hero's swings - its queued combo swings go, a swing before its hit frame stops.
                var queuedSwings = npc.Props.AnimationQueue.Count;
                var isOnlySwingsQueued = queuedSwings > 0 &&
                                         npc.Props.AreAllQueued(i => i is AttackPlayAni);
                if (queuedSwings > 0 && !(isOnlySwingsQueued))
                    continue;

                var interrupted = npc.Props.CurrentAction switch
                {
                    Wait wait => Interrupt(wait),
                    AttackPlayAni { Move: FightAiMove.Strafe } strafe => strafe.TryInterruptStrafe(),
                    AttackPlayAni attack =>
                        attack.TryInterruptSwing() | isOnlySwingsQueued,
                    _ => false
                };
                if (!interrupted)
                    continue;

                if (isOnlySwingsQueued)
                    npc.Props.AnimationQueue.Clear();
                Logger.Log($"[Prehit] {npc.Instance.GetName(NpcNameSlot.Slot0)}: {npc.Props.CurrentAction?.GetType().Name} " +
                           $"cut short by the hero's swing ({queuedSwings} queued swing(s) dropped)", LogCat.Fight);
            }

            static bool Interrupt(Wait wait)
            {
                wait.Interrupt();
                return true;
            }
        }

        /// <summary>
        /// VR reports that an NPC's weapon (or fist) touched the hero's held
        /// weapon while he blocks (HeroBlockDomain).
        /// </summary>
        public void ReportBlockContact(NpcContainer attacker)
        {
            _heroBlockDomain.ReportBlockContact(attacker);
        }

        /// <summary>
        /// Fall damage (NpcJumpFall). Like the engine a fall can kill.
        /// </summary>
        private void OnFallDamage(NpcContainer npc, int damage)
        {
            if (npc?.Vob == null || npc.Props.BodyState == VmGothicEnums.BodyState.BsDead || IsHeroInGodMode(npc))
                return;

            var hitPoints = npc.Vob.GetAttribute((int)NpcAttribute.HitPoints) - damage;
            npc.Vob.SetAttribute((int)NpcAttribute.HitPoints, hitPoints);
            npc.Instance.SetAttribute(NpcAttribute.HitPoints, hitPoints);
            Logger.Log($"[FightService] {npc.Instance.GetName(NpcNameSlot.Slot0)} fall damage {damage} -> HP {hitPoints}", LogCat.Fight);

            if (hitPoints > 0)
                return;

            Kill(npc, npc);
        }

        /// <summary>
        /// Marvin's "kill" - like a fall, the NPC is its own killer (ZS_Dead runs, the hero gets no experience).
        /// </summary>
        private void OnCheatKill(NpcContainer npc)
        {
            if (npc?.Vob == null || npc.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                return;

            npc.Vob.SetAttribute((int)NpcAttribute.HitPoints, 0);
            npc.Instance.SetAttribute(NpcAttribute.HitPoints, 0);
            Kill(npc, npc);
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

            // Only a zapped victim (chain lightning) dies by its caster - B_KillNpc (B_ClearFMC) from a dialog had the
            // hero as `other` and paid him the victim's experience.
            var other = (_gameStateService.GothicVm?.GlobalOther as NpcInstance)?.GetUserData();
            var isZapped = victim.Vob?.CurrentStateName?.StartsWith("ZS_ZAPPED",
                System.StringComparison.OrdinalIgnoreCase) == true;
            if (isZapped && other != null && other != victim && victim.SummonedBy == null)
            {
                OnScriptDamageDepleted(victim, other);
                return;
            }

            Logger.Log($"[FightService] {victim.Instance.GetName(NpcNameSlot.Slot0)} lost its last hit points " +
                       "outside of a hit - dies", LogCat.Fight);
            Kill(victim, victim);
        }

        private bool IsHeroInGodMode(NpcContainer npc)
        {
            return _playerService.IsGodMode && npc?.PrefabProps != null && npc.PrefabProps.IsHero();
        }

        /// <summary>
        /// Chain lightning: a zapped victim (ZS_Zapped: B_MagicHurtNpc every second) out of hit
        /// points dies like the engine's (OpenGothic: script damage kills, only melee knocks out).
        /// </summary>
        public void OnScriptDamageDepleted(NpcContainer victim, NpcContainer attacker)
        {
            if (victim?.Props == null || victim.IsDown() ||
                (victim.PrefabProps != null && victim.PrefabProps.IsHero()))
                return;

            Logger.Log($"[FightService] {victim.Instance.GetName(NpcNameSlot.Slot0)} zapped to death", LogCat.Fight);
            Kill(victim, attacker);
        }

        /// <summary>
        /// The chain lightning found nobody else and zaps the hero himself (magic damage, his protection counts) -
        /// down at 0 like any hit. This follows the script comment "Abbruchlogik selber Treffen" in
        /// Spell_ChainLightning.d and is unverified against the engine.
        /// </summary>
        public void ApplySelfSpellDamage(NpcContainer hero, int damage)
        {
            if (hero?.Props == null || hero.Props.BodyState is VmGothicEnums.BodyState.BsDead
                    or VmGothicEnums.BodyState.BsUnconscious)
                return;

            if (_meleeDamageDomain.ApplyHit(hero, hero, damage, (int)DamageType.Magic))
            {
                Logger.LogWarning("[FightService] The hero zapped himself down", LogCat.Fight);
                _knockoutDomain.KnockOutHero(hero, hero);
            }
        }

        private void OnHit(NpcContainer attacker, NpcContainer target, Vector3 __) =>
            OnHit(attacker, target, __, damageOverride: null);

        /// <summary>
        /// A spell or projectile hit (SpellHitDomain, RangedHitDomain): its damage and protection index.
        /// </summary>
        private void ApplyHit(NpcContainer attacker, NpcContainer target, Vector3 position, int damage,
            int protectionIndex)
        {
            OnHit(attacker, target, position, damage, protectionIndex);
        }

        private void OnHit(NpcContainer attacker, NpcContainer target, Vector3 position, int? damageOverride,
            int overrideProtectionIndex = (int)DamageType.Fire)
        {
            if (target.Props.IsDead)
                return;

            var isHero = target.Instance.IsHero(_gameStateService.GothicVm);
            if (IsHitIgnored(attacker, target, isHero))
                return;

            if (!isHero && target.IsKnockedOut())
            {
                OnHitOnKnockedOut(attacker, target, position, damageOverride);
                return;
            }

            // Hero is being hit — alert party members immediately. We already know isHero here so no
            // comparison needed; attacker is directly the enemy party members should engage.
            if (isHero)
                _damagePerceptionDomain.AlertPartyMembersToDefendHero(attacker);

            var isHeroAttacker = attacker.Instance.IsHero(_gameStateService.GothicVm);

            if (IsHitBlocked(attacker, target, position, isHero, isHeroAttacker, damageOverride))
                return;

            // Blood only now - not for a parried or blocked hit.
            if (damageOverride == null)
                GlobalEventDispatcher.MeleeHitLanded.Invoke(attacker, target, position);

            Logger.Log($"[FightService.OnHit] *** {attacker.Instance.GetName(NpcNameSlot.Slot0)} HIT " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
            if (_meleeDamageDomain.ApplyHit(attacker, target, damageOverride, overrideProtectionIndex))
                GoDown(attacker, target, isHero, damageOverride);
            else
                ReactToHurt(attacker, target, isHeroAttacker);

            _damagePerceptionDomain.BroadcastDamagePerceptions(attacker, target);
        }

        /// <summary>
        /// Hits that don't count at all: in a dialog, on the knocked out hero.
        /// </summary>
        private bool IsHitIgnored(NpcContainer attacker, NpcContainer target, bool isHero)
        {
            // The two in a dialog can't hurt each other - others can still hit them (the engine has no dialog check).
            if (IsInCurrentDialog(attacker) || IsInCurrentDialog(target))
            {
                Logger.Log("[FightService] Hit blocked — dialog in progress", LogCat.Fight);
                return true;
            }

            // Like the engine (OpenGothic takeDamage: isDown) the knocked out hero takes no damage - only a finishing
            // move kills him (OnFinishingMove).
            if (isHero && target.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return true;

            return false;
        }

        /// <summary>
        /// Like the engine (OpenGothic takeDamage returns if isDown()) a knocked out NPC takes no damage - arrows,
        /// spells and other hits pass (summons stop at him anyway: ZS_MM_Attack_Loop C_NpcIsDown). Only a finishing
        /// move kills him: the NPCs' AI_FinishingMove, and the hero's melee hit (VR: his action key) when he could
        /// finish (IsHeroFinishingMove). Both go through FightFinishingMove.
        /// </summary>
        private void OnHitOnKnockedOut(NpcContainer attacker, NpcContainer target, Vector3 position,
            int? damageOverride)
        {
            if (!IsHeroFinishingMove(attacker, target, damageOverride))
            {
                Logger.Log($"[FightService] {attacker.Instance.GetName(NpcNameSlot.Slot0)}'s hit doesn't hurt " +
                           $"knocked out {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
                return;
            }

            // Not the swing that knocked him out: a VR weapon can enter several hitboxes in one swing - Bullit died
            // instead of lying down.
            if (_knockoutDomain.IsInKnockoutGraceTime(target))
                return;

            GlobalEventDispatcher.MeleeHitLanded.Invoke(attacker, target, position);
            GlobalEventDispatcher.FightFinishingMove.Invoke(attacker, target);
        }

        /// <summary>
        /// The hero can finish a knocked out NPC like the engine (OpenGothic Npc::canFinish): a one- or two-handed
        /// weapon drawn, the target within NPC_ATTACK_FINISH_DISTANCE. Fists, arrows and spells can't.
        /// </summary>
        private bool IsHeroFinishingMove(NpcContainer attacker, NpcContainer target, int? damageOverride)
        {
            if (damageOverride.HasValue || !attacker.Instance.IsHero(_gameStateService.GothicVm) ||
                (VmGothicEnums.WeaponState)attacker.Vob.FightMode is not (VmGothicEnums.WeaponState.W1H or
                    VmGothicEnums.WeaponState.W2H) ||
                attacker.Go == null || target.Go == null)
                return false;

            var toTarget = Vector3.ProjectOnPlane(target.Go.transform.position - attacker.Go.transform.position,
                Vector3.up);
            return toTarget.magnitude <= _npcAiService.GetFinishDistanceMeters();
        }

        /// <summary>
        /// A parry or block takes the hit (HeroBlockDomain).
        /// </summary>
        private bool IsHitBlocked(NpcContainer attacker, NpcContainer target, Vector3 position, bool isHero,
            bool isHeroAttacker, int? damageOverride)
        {
            // The hero's hit on a parrying / jumping back NPC.
            if (!isHero && isHeroAttacker && _heroBlockDomain.TryBlockNpcHit(attacker, target))
                return true;

            // A parry is its own state like the engine's - no hits while blocking.
            if (isHeroAttacker && damageOverride == null && _heroBlockDomain.IsHeroBlocking(attacker))
            {
                Logger.Log($"[FightService] The hero blocks - his hit on {target.Instance.GetName(NpcNameSlot.Slot0)} " +
                           "doesn't count", LogCat.Fight);
                return true;
            }

            // The hero's parry: an NPC's melee hit (no damage override: not a spell or projectile).
            // A hit that missed the block lands a moment later.
            return isHero && damageOverride == null &&
                   _heroBlockDomain.TryBlockHeroHit(attacker, target, () => OnHit(attacker, target, position, null));
        }

        /// <summary>
        /// The hit took the last hit points. The hero goes down by the same rule as everybody (OpenGothic
        /// checkHealth): monsters and humans hostile to him kill him, the others knock him out. A berzerk is always
        /// only stopped. Like the engine (OpenGothic takeDamage: dontKill only without a bullet or spell) arrows, bolts
        /// and spells (they pass their damage) always kill - the hero too, only melee knocks out. A knockout by
        /// the hero's summon counts as the hero's (GetCreditedAttacker); a kill is the summon's own - ZS_Dead pays the
        /// hero through its AIV_MM_PARTYMEMBER like the engine (OpenGothic: other = lastHit).
        /// </summary>
        private void GoDown(NpcContainer attacker, NpcContainer target, bool isHero, int? damageOverride)
        {
            var isMelee = !damageOverride.HasValue;
            var isKnockedOut = isMelee && (isHero
                ? _knockoutDomain.IsKnockout(attacker, target)
                : target.IsHuman() &&
                  (DamagePerceptionDomain.IsBerzerk(target) ||
                   _knockoutDomain.IsKnockout(_damagePerceptionDomain.GetCreditedAttacker(attacker), target)));
            if (isHero && isKnockedOut)
            {
                Logger.LogWarning("[FightService] Hero knocked out!", LogCat.Fight);
                _knockoutDomain.KnockOutHero(target, attacker);
            }
            else if (isKnockedOut)
            {
                Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is UNCONSCIOUS",
                    LogCat.Npc);
                DropReadiedWeapon(target);
                _knockoutDomain.KnockOutNpc(target, _damagePerceptionDomain.GetCreditedAttacker(attacker));
            }
            else
            {
                Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is DEAD",
                    LogCat.Npc);
                Kill(target, attacker);
            }
        }

        /// <summary>
        /// The hit hurt but didn't take the last hit points: stumble and pain sound.
        /// </summary>
        private void ReactToHurt(NpcContainer attacker, NpcContainer target, bool isHeroAttacker)
        {
            // An NPC's swing isn't cut short by another NPC's hit - two bandits taking turns kept Cavalorn from ever
            // landing one (stumble before his hit frame). The hero's hits still interrupt like in Gothic.
            if (!isHeroAttacker && target.Props.CurrentAction is AttackPlayAni { IsSwingInProgress: true })
            {
                Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} took damage " +
                           "mid-swing - keeps swinging", LogCat.Npc);
            }
            else
            {
                Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} took damage, " +
                           "playing hurt animation", LogCat.Npc);
                OnHitChangeAnimation(target);
            }
            OnHitPlaySound(target);
        }

        private void OnFinishingMove(NpcContainer attacker, NpcContainer target)
        {
            var isHero = target.PrefabProps != null && target.PrefabProps.IsHero();
            if (isHero)
            {
                OnHeroDied(target, attacker);
                return;
            }

            Logger.Log($"[FightService.FinishingMove] {attacker.Instance.GetName(NpcNameSlot.Slot0)} executes {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
            var wasLying = target.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious;
            // He dies where he lies, with his death scream.
            var isLyingDeath = wasLying && TryPlayLyingDeath(target);
            if (isLyingDeath)
                PlayDeathScream(target);
            Kill(target, attacker, isLyingDeath);
        }

        /// <summary>
        /// An NPC finishing the knocked out hero off kills him like the engine (OpenGothic Npc::finishingMove - hit
        /// points 0, then PERC_ASSESSMURDER). The hero stays locked; the VR body falls dead and the menu opens
        /// (VRHeroBody).
        /// </summary>
        private void OnHeroDied(NpcContainer hero, NpcContainer attacker)
        {
            if (hero.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                return;

            Logger.LogWarning($"[FightService] Hero finished off by {attacker.Instance.GetName(NpcNameSlot.Slot0)} - " +
                              "DEAD", LogCat.Fight);
            // The same death as any other (PERC_ASSESSMURDER, EndGame: locked, the death scream, the menu offers loading
            // only).
            Kill(hero, attacker);
        }

        /// <summary>
        /// Like the engine (OpenGothic Npc's death: emitSoundSVM("SVM_%d_DEAD")) - the death scream (G1 hero: voice 15).
        /// The hero from his own voice source (Output plays his lines there), NPCs from theirs.
        /// </summary>
        private void PlayDeathScream(NpcContainer npc)
        {
            if (npc.Instance.Voice <= 0)
                return;
            var clip = _audioService.GetRandomSoundClip($"SVM_{npc.Instance.Voice}_DEAD");
            if (clip == null)
            {
                Logger.LogWarning($"[FightService] No SVM_{npc.Instance.Voice}_DEAD for " +
                                  $"{npc.Instance.GetName(NpcNameSlot.Slot0)}'s death", LogCat.Fight);
                return;
            }

            AudioSource source = null;
            if (npc.PrefabProps != null && npc.PrefabProps.IsHero())
            {
                var heroGo = _npcService.GetHeroGameObject();
                source = heroGo != null ? heroGo.GetComponent<AudioSource>() : null;
            }
            if (source == null && npc.PrefabProps != null)
                source = npc.PrefabProps.NpcSound;
            if (source != null)
                source.PlayOneShot(clip);
        }

        // From lying unconscious (S_WOUNDED, on the belly; S_WOUNDEDB on
        // the back) to lying dead - the transition if the model has one, else straight into the dead loop.
        private static readonly string[][] _lyingDeathAnimations =
        {
            new[] { "T_WOUNDED_2_DEAD", "S_DEAD" },
            new[] { "T_WOUNDEDB_2_DEADB", "S_DEADB" }
        };

        private bool TryPlayLyingDeath(NpcContainer target)
        {
            var animationSystem = target.PrefabProps?.AnimationSystem;
            if (animationSystem == null)
                return false;

            target.Props.AnimationQueue.Clear();
            _physicsService.DisablePhysicsForNpc(target.PrefabProps);
            foreach (var animations in _lyingDeathAnimations)
            {
                foreach (var animation in animations)
                {
                    if (!animationSystem.HasAnimation(animation))
                        continue;
                    animationSystem.StopAllAnimations();
                    animationSystem.PlayAnimation(animation);
                    Logger.Log($"[FightService.FinishingMove] {target.Instance.GetName(NpcNameSlot.Slot0)} dies lying " +
                               $"({animation})", LogCat.Npc);
                    return true;
                }
            }

            Logger.LogWarning($"[FightService.FinishingMove] {target.Instance.GetName(NpcNameSlot.Slot0)}: no lying death " +
                              "animation - falls like a standing death", LogCat.Npc);
            return false;
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

        private const float _cannotUseMessageSeconds = 2f;
        private float _lastCannotUseMessageTime = float.MinValue;

        /// <summary>
        /// Does the hero meet a weapon's requirements (cond_atr/cond_value)?
        /// If not, like the engine's equip (OpenGothic Inventory::use -> printCannotUseError) the scripts' G_CanNotUse
        /// prints what is missing ("... strength points") - at most every few seconds. Melee weapons still hit (for 1),
        /// bows and crossbows don't shoot.
        /// </summary>
        public bool CheckHeroWeaponRequirement(ItemInstance weapon, bool isMessageShown)
        {
            if (weapon == null)
                return true;

            var hero = _npcService.GetHeroContainer();
            if (hero == null || MeleeDamageDomain.IsWeaponRequirementMet(hero, weapon, out var requirement, out var attribute, out var value))
                return true;

            if (isMessageShown && Time.time - _lastCannotUseMessageTime > _cannotUseMessageSeconds)
            {
                _lastCannotUseMessageTime = Time.time;
                Logger.Log($"[FightService] The hero lacks {requirement} for '{weapon.Name}'", LogCat.Fight);
                PrintCannotUse(hero, attribute, value);
            }
            return false;
        }

        // Gothic's NPC_TALENT_MAGE (G1/G2: 7) - read from the scripts, the talent enums of the games differ.
        private const int _defaultMageTalent = 7;

        /// <summary>
        /// Does the hero know the rune's magic circle (C_ITEM.mag_circle vs
        /// NPC_TALENT_MAGE)? If not, like the engine the scripts' G_CanNotCast says how many circles are missing - at
        /// most every few seconds. Such a rune can't be equipped nor cast; scrolls have no circle.
        /// </summary>
        public bool CheckHeroRuneCircle(ItemInstance rune, bool isMessageShown)
        {
            if (rune == null || rune.MagCircle <= 0)
                return true;

            var hero = _npcService.GetHeroContainer();
            var vm = _gameStateService.GothicVm;
            if (hero == null || vm == null)
                return true;
            var mageTalent = vm.GetSymbolByName(DaedalusConst.NpcTalentMage)?.GetInt(0) ?? _defaultMageTalent;
            var circle = _npcHelperService.ExtNpcGetTalentSkill(hero.Instance, mageTalent);
            if (circle >= rune.MagCircle)
                return true;

            if (isMessageShown && Time.time - _lastCannotUseMessageTime > _cannotUseMessageSeconds)
            {
                _lastCannotUseMessageTime = Time.time;
                Logger.Log($"[FightService] The hero knows circle {circle}, '{rune.Description}' needs {rune.MagCircle}",
                    LogCat.Fight);
                var function = vm.GetSymbolByName(DaedalusConst.GCanNotCast);
                if (function != null)
                {
                    var oldSelf = vm.GlobalSelf;
                    vm.GlobalSelf = hero.Instance;
                    try
                    {
                        vm.Call<int, int, int>(function.Index, 1, rune.MagCircle, circle);
                    }
                    finally
                    {
                        vm.GlobalSelf = oldSelf;
                    }
                }
            }
            return false;
        }

        private void PrintCannotUse(NpcContainer hero, int attribute, int value)
        {
            var vm = _gameStateService.GothicVm;
            var function = vm.GetSymbolByName(DaedalusConst.GCanNotUse);
            if (function == null)
                return;

            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = hero.Instance;
            try
            {
                vm.Call<int, int, int>(function.Index, 1, attribute, value);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }
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

        /// <summary>
        /// ZS_Dead of a corpse loaded from a save (AiHandler: dead on spawn) - its quest variables are set in the fresh
        /// VM (NpcDeathDomain).
        /// </summary>
        public void RunZsDead(NpcInstance dead, NpcContainer killer)
        {
            _deathDomain.RunZsDead(dead, killer);
        }

        /// <summary>
        /// The NPC dies: dead state, AI queue dropped, the dying animation (NpcDeathDomain), then ZS_Dead with the killer.
        /// The hero's death ends the game (EndGame).
        /// </summary>
        private void Kill(NpcContainer victim, NpcContainer killer, bool isDyingAnimationPlayed = false)
        {
            DropReadiedWeapon(victim);
            if (isDyingAnimationPlayed)
                victim.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            else
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
            _contextInteractionService.LockPlayer(PlayerLocks.Death, _knockoutDomain.GetDownLockedControls());
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

            // The stumble may push the NPC over an edge - the sleeping fall check has to see it.
            _npcJumpFallFactory.Wake(target);
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
