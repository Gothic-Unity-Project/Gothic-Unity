using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// The one owner of how an NPC goes down: a melee hit (damage: MeleeDamageDomain), the hit points gone otherwise
    /// (HitPointsDepleted), knockout (NpcKnockoutDomain) or death (NpcDeathDomain), the hero's death ending the game.
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
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly Gothic.Core.Services.Config.ConfigService _fightConfigService;
        [Inject] private readonly Gothic.Core.Services.Meshes.ParticleService _particleService;
        [Inject] private readonly Gothic.Core.Services.Player.PlayerService _playerService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ContextRangedCombatService _contextRangedCombatService;
        [Inject] private readonly Gothic.Core.Services.Caches.VmCacheService _vmCacheService;
        [Inject] private readonly Gothic.Core.Services.Meshes.MeshService _meshService;
        [Inject] private readonly INpcJumpFallFactory _npcJumpFallFactory;

        private readonly NpcDeathDomain _deathDomain = new NpcDeathDomain().Inject();
        private readonly NpcKnockoutDomain _knockoutDomain = new NpcKnockoutDomain().Inject();
        private readonly MeleeDamageDomain _meleeDamageDomain = new MeleeDamageDomain().Inject();

        // DAM_FIRE in Constants.d (bit after DAM_EDGE).
        private const int _damFire = 8;

        public void Init()
        {
            // Scripts (B_KillNpc), drowning and damage zones kill with or without the combat system.
            GlobalEventDispatcher.HitPointsDepleted.AddListener(OnHitPointsDepleted);
            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);

            if (!_configService.Dev.EnableCombatSystem)
                return;

            GlobalEventDispatcher.FightHit.AddListener(OnHit);
            GlobalEventDispatcher.SpellHit.AddListener(OnSpellHit);
            GlobalEventDispatcher.SpellSpreadHit.AddListener((caster, pos, damage) => ApplySpreadHit(caster, pos, damage, null));
            GlobalEventDispatcher.FallDamage.AddListener(OnFallDamage);
            GlobalEventDispatcher.CheatKill.AddListener(OnCheatKill);
            GlobalEventDispatcher.RangedHit.AddListener(OnRangedHit);
            GlobalEventDispatcher.FightFinishingMove.AddListener(OnFinishingMove);
            GlobalEventDispatcher.HeroMeleeSwing.AddListener(OnHeroMeleeSwing);
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
                if (npc == hero || npc?.Go == null || npc.Props == null || NpcHelperService.IsDown(npc))
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
                                         npc.Props.AnimationQueue.All(i => i is AttackPlayAni);
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
        /// OpenGothic Npc::takeDamage - a parry facing the attacker
        /// (30 degrees) or a jump back (90 degrees) takes no damage, weapons clash (CS_IAI_ sound + CPFX_IAI_ effect).
        /// The whole parry/jump back animation counts (the engine: its DEF_WINDOW frames).
        /// </summary>
        private bool TryBlockHit(NpcContainer attacker, NpcContainer target)
        {
            if (target.Go == null || attacker.Go == null ||
                target.Props.CurrentAction is not AttackPlayAni defence)
                return false;

            var isParry = defence.Move == FightAiMove.Parry &&
                          Gothic.Core.Domain.Npc.Actions.AnimationActions.Attack.IsInAngle(target.Go.transform,
                              attacker.Go.transform.position, FightConst.FocusAngle);
            var isJumpBack = defence.Move == FightAiMove.JumpBack &&
                             Gothic.Core.Domain.Npc.Actions.AnimationActions.Attack.IsInAngle(target.Go.transform,
                                 attacker.Go.transform.position, FightConst.JumpBackAngle);
            if (!isParry && !isJumpBack)
                return false;

            Logger.Log($"[FightService] {target.Instance.GetName(NpcNameSlot.Slot0)} " +
                       $"{(isParry ? "parries" : "jumps back from")} {attacker.Instance.GetName(NpcNameSlot.Slot0)}'s hit",
                LogCat.Fight);
            if (isParry)
                PlayWeaponBlockEffect(attacker, target);
            return true;
        }

        /// <summary>
        /// DeveloperConfig.EnableVrHeroBlock: OpenGothic Npc::takeDamage - the blocking hero takes no damage from a
        /// human's hit (fists too) or a monster's with a drawn weapon (orcs, skeletons) from the front (HeroFocusAngle
        /// around the head's direction). Animals, spells and projectiles always hit (they don't come this way).
        /// </summary>
        private bool TryHeroBlockHit(NpcContainer attacker, NpcContainer hero, Vector3 position)
        {
            if (!_configService.Dev.EnableVrHeroBlock || !hero.Props.IsBlocking || attacker.Go == null ||
                hero.Go == null || _isBlockDecided)
                return false;

            var attackerWeapon = (VmGothicEnums.WeaponState)attacker.Vob.FightMode;
            if (!attacker.IsHuman() && attackerWeapon is not (VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H))
                return false;

            // Only a hit that touched the held weapon is blocked - a block
            // held above the NPC's head blocked his hit too. The hit can reach the body first: it waits a moment for
            // the touch.
            if (HasBlockContact(attacker))
            {
                BlockHit(attacker, hero);
                return true;
            }
            _unityMonoService.StartCoroutine(AwaitBlockContact(attacker, hero, position));
            return true;
        }

        private const float _blockContactSeconds = 0.5f;
        private readonly Dictionary<NpcContainer, float> _blockContacts = new();
        // A deferred hit resolved without a block goes through OnHit again - without asking for a block twice.
        private bool _isBlockDecided;

        /// <summary>
        /// VR reports that an NPC's weapon (or fist) touched the hero's held
        /// weapon while he blocks.
        /// </summary>
        public void ReportBlockContact(NpcContainer attacker)
        {
            if (attacker != null)
                _blockContacts[attacker] = Time.time;
        }

        private bool HasBlockContact(NpcContainer attacker)
        {
            return _blockContacts.TryGetValue(attacker, out var time) && Time.time - time <= _blockContactSeconds;
        }

        /// <summary>
        /// The hit reached the hero before his weapon: blocked if the NPC's weapon touches it within
        /// _blockContactSeconds, else it hits now.
        /// </summary>
        private IEnumerator AwaitBlockContact(NpcContainer attacker, NpcContainer hero, Vector3 position)
        {
            var end = Time.time + _blockContactSeconds;
            while (Time.time < end)
            {
                if (HasBlockContact(attacker) && hero.Props.IsBlocking)
                {
                    BlockHit(attacker, hero);
                    yield break;
                }
                yield return null;
            }

            Logger.Log($"[FightService] {attacker.Instance.GetName(NpcNameSlot.Slot0)}'s hit missed the blocking weapon",
                LogCat.Fight);
            _isBlockDecided = true;
            try
            {
                OnHit(attacker, hero, position, null);
            }
            finally
            {
                _isBlockDecided = false;
            }
        }

        private void BlockHit(NpcContainer attacker, NpcContainer hero)
        {
            Logger.Log($"[FightService] The hero blocks {attacker.Instance.GetName(NpcNameSlot.Slot0)}'s hit", LogCat.Fight);
            PlayWeaponBlockEffect(attacker, hero);
            GlobalEventDispatcher.HeroBlockedHit.Invoke(attacker);
        }

        /// <summary>
        /// OpenGothic World::addWeaponBlkEffect: "CS_IAI_" + both weapons' materials (item attacks item), the particle
        /// effect CPFX_IAI_ the same or by the defender's material. Without a weapon on either side there's nothing.
        /// G1 has only CS_IAI_ME_ME - other pairs take the generic CS_IAI.
        /// </summary>
        private void PlayWeaponBlockEffect(NpcContainer attacker, NpcContainer target)
        {
            // The hero's held weapon isn't equipped in VR - the material of the swing (-1: fists).
            var attackerMaterial = attacker.PrefabProps != null && attacker.PrefabProps.IsHero()
                ? attacker.Props.MeleeSwingMaterial
                : _npcHelperService.ExtNpcGetEquippedMeleeWeapon(attacker.Instance)?.Material ?? -1;
            // The blocking hero's held weapon isn't equipped either (DeveloperConfig.EnableVrHeroBlock).
            var defenderMaterial = target.PrefabProps != null && target.PrefabProps.IsHero()
                ? target.Props.BlockWeaponMaterial
                : (_npcHelperService.ExtNpcGetEquippedMeleeWeapon(target.Instance) ??
                   (target.Props.CurrentItem >= 0 ? _vmCacheService.TryGetItemData(target.Props.CurrentItem) : null))
                ?.Material ?? -1;
            if (attackerMaterial < 0 || defenderMaterial < 0)
            {
                Logger.Log($"[FightService] Parry without a clash: attacker material {attackerMaterial}, " +
                           $"defender material {defenderMaterial}", LogCat.Fight);
                return;
            }

            var from = GetMaterialTag(attackerMaterial);
            var to = GetMaterialTag(defenderMaterial);
            var clip = _audioService.GetRandomSoundClip($"CS_IAI_{from}_{to}") ?? _audioService.GetRandomSoundClip("CS_IAI");
            if (clip != null && target.PrefabProps?.NpcSound != null)
                target.PrefabProps.NpcSound.PlayOneShot(clip);

            var pfxName = $"CPFX_IAI_{from}_{to}";
            if (_vmCacheService.TryGetPfxData(pfxName) == null)
                pfxName = to switch { "WO" => "CPFX_IAI_WOOD", "ST" => "CPFX_IAI_STONE", _ => "CPFX_IAI_METAL" };
            if (_vmCacheService.TryGetPfxData(pfxName) == null)
                return;

            var position = target.Go.transform.position + Vector3.up * 1.3f +
                           (attacker.Go.transform.position - target.Go.transform.position).normalized * 0.4f;
            _meshService.CreateVobPfx(pfxName, position, Quaternion.identity, destroyAfterPlay: true);
        }

        private static string GetMaterialTag(int material)
        {
            return (VmGothicEnums.ItemMaterial)material switch
            {
                VmGothicEnums.ItemMaterial.Wood => "WO",
                VmGothicEnums.ItemMaterial.Stone => "ST",
                VmGothicEnums.ItemMaterial.Leather => "LE",
                VmGothicEnums.ItemMaterial.Clay => "CL",
                VmGothicEnums.ItemMaterial.Glass => "GL",
                _ => "ME"
            };
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
        /// DeveloperConfig.EnableMarvinKill: Marvin's "kill" - like a fall, the NPC is its own killer (ZS_Dead runs, the
        /// hero gets no experience).
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

        private void OnSpellHit(NpcContainer caster, NpcContainer target, Vector3 pos, int damage)
        {
            var mfxName = GetSpellMfxName(caster.ActiveSpell);

            // DeveloperConfig.EnableVrChainLightning: chain lightning zaps its victims one by one (VRChainLightning, the
            // scripts' ZS_Zapped does the damage) - not everyone around the caster at once.
            var isChainLightning = _configService.Dev.EnableVrChainLightning &&
                                   string.Equals(mfxName, _chainLightningName, System.StringComparison.OrdinalIgnoreCase);
            if (mfxName != null && SpellConst.AoeEffectNames.Contains(mfxName) && !isChainLightning)
            {
                var rangeM = GetAoeRangeMeters(mfxName);
                var casterPos = caster.Go.transform.position;
                Logger.Log($"[FightService.SpellHit] {caster.Instance.GetName(NpcNameSlot.Slot0)} casts AOE '{mfxName}' (range={rangeM:F1}m) dmg={damage}", LogCat.Fight);

                var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
                var awakeHero = _npcService.GetHeroContainer();
                for (var awakeIdx = -1; awakeIdx < awakeNpcs.Count; awakeIdx++)
                {
                    var candidate = awakeIdx < 0 ? awakeHero : awakeNpcs[awakeIdx];
                    if (candidate == null) continue;
                    if (candidate == caster || candidate.Go == null || !candidate.Go.activeInHierarchy)
                        continue;
                    if (IsSparedByAreaSpell(candidate))
                        continue;
                    if (Vector3.Distance(casterPos, candidate.Go.transform.position) > rangeM)
                        continue;

                    ApplySpellHit(caster, candidate, candidate.Go.transform.position, damage);
                    TryThrowBack(caster, candidate, mfxName, damage);
                    // The spell's hit effect on everyone it reaches (ice wave: ice).
                    _particleService.PlaySpellHitFx(mfxName, candidate);
                }
                return;
            }

            ApplySpellHit(caster, target, pos, damage);
            TryThrowBack(caster, target, mfxName, damage);

            if (mfxName != null && SpellConst.SpreadEffectNames.Contains(mfxName))
                ApplySpreadHit(caster, pos, damage, target);
        }

        /// <summary>
        /// The burning area of a fire storm (spellFX_Firestorm_SPREAD) damages
        /// everyone within SpellConst.SpreadRangeMeters of the impact - only the NPC hit directly took damage, one
        /// right next to the flames on the ground nothing.
        /// </summary>
        private void ApplySpreadHit(NpcContainer caster, Vector3 pos, int damage, NpcContainer directTarget)
        {
            if (caster == null)
                return;

            var mfxName = GetSpellMfxName(caster.ActiveSpell);
            var hitCount = 0;
            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            var awakeHero = _npcService.GetHeroContainer();
            for (var awakeIdx = -1; awakeIdx < awakeNpcs.Count; awakeIdx++)
            {
                var candidate = awakeIdx < 0 ? awakeHero : awakeNpcs[awakeIdx];
                if (candidate == null) continue;
                if (candidate == caster || candidate == directTarget || candidate.Go == null ||
                    !candidate.Go.activeInHierarchy)
                    continue;
                if (IsSparedByAreaSpell(candidate))
                    continue;
                if (Vector3.Distance(pos, candidate.Go.transform.position) > SpellConst.SpreadRangeMeters)
                    continue;

                ApplySpellHit(caster, candidate, candidate.Go.transform.position, damage);
                if (mfxName != null)
                    _particleService.PlaySpellHitFx(mfxName, candidate);
                hitCount++;
            }

            Logger.Log($"[FightService.SpellSpread] {caster.Instance.GetName(NpcNameSlot.Slot0)}'s '{mfxName}' area " +
                       $"({SpellConst.SpreadRangeMeters:F0} m) hit {hitCount} more NPC(s), dmg={damage}", LogCat.Fight);
        }

        private void ApplySpellHit(NpcContainer caster, NpcContainer target, Vector3 pos, int damage)
        {
            Logger.Log($"[FightService.SpellHit] {caster.Instance.GetName(NpcNameSlot.Slot0)} → {target.Instance.GetName(NpcNameSlot.Slot0)} dmg={damage}", LogCat.Fight);

            // PERC_ASSESSMAGIC drives victim-side spell reactions (B_AssessMagic -> ZS_MagicFreeze,
            // ZS_MagicSleep, ZS_Zapped, Fear/Charm/Berzerk, guild immunity checks, ally fight-sound).
            // That's pure Daedalus content dispatched by spell ID, so it covers every "bad" spell
            // (Sleep, Icecube, Fear, Charm, Berzerk, ChainLightning...) without any C# per-spell
            // logic, and works identically for G1/G2 since it's just a symbol lookup by name.
            if (target.Props.BodyState != VmGothicEnums.BodyState.BsDead && !IsOwnSummonHit(caster, target))
            {
                _npcAiService.ExecutePerception(
                    VmGothicEnums.PerceptionType.AssessMagic,
                    target.Props, target.Instance,
                    victim: target.Instance,
                    other: caster.Instance);
            }

            // DeveloperConfig.EnableHeroSpellStates: the hero has no PERC_ASSESSMAGIC - sleep/freeze are set here so the
            // caster's scripts see them (Npc_IsInState) instead of casting again and again.
            if (_fightConfigService.Dev.EnableHeroSpellStates && target.Vob.Player &&
                target.Props.BodyState != VmGothicEnums.BodyState.BsDead)
                _npcAiService.ApplyHeroSpellState(target, GetSpellMfxName(caster.ActiveSpell));

            // Direct damage (Firebolt, Fireball, ...) still goes through the normal hit pipeline for
            // HP/animation/death handling. Reaction-only spells (damage=0) stop here — their effect
            // (freeze, sleep, ...) is entirely driven by the ASSESSMAGIC dispatch above, and routing
            // a fake 0-damage hit through OnHit would wrongly trigger pain animations/sounds.
            // The spell's own damage type (OpenGothic: spl.damage_type) - every
            // spell was taken as fire (Windfist DAM_FLY, lightning DAM_MAGIC).
            var spellDamageType = caster.ActiveSpell >= 0
                ? _vmCacheService.TryGetSpellData(caster.ActiveSpell)?.DamageType ?? 0
                : 0;
            if (damage > 0)
                OnHit(caster, target, pos, damageOverride: damage,
                    overrideProtectionIndex: spellDamageType != 0
                        ? GetProtectionIndex(spellDamageType)
                        : (int)DamageType.Fire);

            // Fire spells set the victim on fire (VOB_BURN + the s_Fire_Victim dance).
            // Not the hero - in VR there's no own body to burn.
            if (damage > 0 && !target.Vob.Player && IsFireSpell(caster.ActiveSpell))
                GlobalEventDispatcher.NpcIgnited.Invoke(target);
        }

        private const string _chainLightningName = "Chainlightning";
        private const float _throwBaseSpeed = 3f;
        private const float _throwMaxSpeed = 8f;
        private const float _throwDamagePerSpeed = 25f;
        private const float _throwUpSpeed = 3.5f;

        /// <summary>
        /// DeveloperConfig.EnableWindfistThrow: Windfist and Stormfist (DAM_FLY) throw the living away from the caster -
        /// the harder the spell, the farther (NpcJumpFall.TryThrow). Not the hero (his body is the player's).
        /// </summary>
        private void TryThrowBack(NpcContainer caster, NpcContainer target, string mfxName, int damage)
        {
            if (!_configService.Dev.EnableWindfistThrow || mfxName == null || caster?.Go == null || target?.Go == null ||
                target == caster || (target.PrefabProps != null && target.PrefabProps.IsHero()) ||
                NpcHelperService.IsDown(target) || !SpellConst.ThrowEffectNames.Contains(mfxName))
                return;

            var away = Vector3.ProjectOnPlane(target.Go.transform.position - caster.Go.transform.position, Vector3.up);
            if (away.sqrMagnitude < 0.0001f)
                away = caster.Go.transform.forward;
            var speed = Mathf.Clamp(_throwBaseSpeed + damage / _throwDamagePerSpeed, _throwBaseSpeed, _throwMaxSpeed);
            _npcJumpFallFactory.Get(target)?.TryThrow(away.normalized * speed, _throwUpSpeed);
        }

        /// <summary>
        /// DeveloperConfig.EnableVrChainLightning: a zapped victim (ZS_Zapped: B_MagicHurtNpc every second) out of hit
        /// points dies like the engine's (OpenGothic: script damage kills) - or is knocked out with the "Ranged knockout"
        /// option (humans).
        /// </summary>
        public void OnScriptDamageDepleted(NpcContainer victim, NpcContainer attacker)
        {
            if (victim?.Props == null || NpcHelperService.IsDown(victim) ||
                (victim.PrefabProps != null && victim.PrefabProps.IsHero()))
                return;

            if (IsRangedKnockoutOn() && _knockoutDomain.IsKnockout(attacker, victim))
            {
                Logger.Log($"[FightService] {victim.Instance.GetName(NpcNameSlot.Slot0)} zapped down - UNCONSCIOUS",
                    LogCat.Fight);
                DropReadiedWeapon(victim);
                _knockoutDomain.KnockOutNpc(victim, attacker);
                return;
            }

            Logger.Log($"[FightService] {victim.Instance.GetName(NpcNameSlot.Slot0)} zapped to death", LogCat.Fight);
            Kill(victim, attacker);
        }

        /// <summary>
        /// DeveloperConfig.EnableVrChainLightning: the chain lightning found nobody else and zaps the hero himself
        /// (magic damage, his protection counts) - down at 0 like any hit.
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

        private bool IsFireSpell(int spellId)
        {
            var damageType = spellId < 0 ? 0 : _vmCacheService.TryGetSpellData(spellId)?.DamageType ?? 0;
            return (damageType & _damFire) != 0;
        }

        /// <summary>
        /// Engine ranged damage (OpenGothic DamageCalculator::rangeDamageValue): G2 DEX + weapon damage, G1 weapon
        /// damage only, minus the protection of the weapon's damage type (arrows/bolts: PROT_POINT).
        /// The hit chance roll is skipped - a VR projectile that physically hit, hit.
        /// FIXME - G1 crit (TALENT_BOW/CROSSBOW value % chance) together with the melee talent formula.
        /// </summary>
        private void OnRangedHit(NpcContainer shooter, NpcContainer target, Vector3 pos,
            (ItemInstance weapon, float damageScale) shot)
        {
            var (weapon, damageScale) = shot;
            if (shooter == null || target == null || weapon == null)
                return;

            var damage = _contextRangedCombatService.GetRangedDamage(shooter, weapon);
            damage = Mathf.RoundToInt(damage * Mathf.Clamp01(damageScale));

            var protectionIndex = GetProtectionIndex(weapon.DamageType);
            Logger.Log($"[FightService.RangedHit] {shooter.Instance.GetName(NpcNameSlot.Slot0)} -> " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)} raw={damage} prot[{protectionIndex}]", LogCat.Fight);

            OnHit(shooter, target, pos, damage, protectionIndex);
        }

        private string GetSpellMfxName(int spellId)
        {
            if (spellId < 0) return null;
            var sym = _gameStateService.GothicVm.GetSymbolByName("spellFXInstanceNames");
            var name = sym?.GetString((ushort)spellId);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>
        /// AOE radius for a spell. Chainlightning/Stormfist expose it as a standalone SPL_RANGE_*
        /// const; Icewave/Firerain/Massdeath hardcode targetCollectRange=1000 directly on their
        /// C_Spell_Proto instance in vanilla Gothic (not readable as a symbol), so those fall back
        /// to SpellConst.DefaultAoeRangeCm, which matches that vanilla value.
        /// </summary>
        private float GetAoeRangeMeters(string mfxName)
        {
            var sym = _gameStateService.GothicVm.GetSymbolByName($"SPL_RANGE_{mfxName.ToUpper()}");
            var rangeCm = sym?.GetInt(0) ?? SpellConst.DefaultAoeRangeCm;
            return rangeCm / 100f;
        }

        private void OnHit(NpcContainer attacker, NpcContainer target, Vector3 __) =>
            OnHit(attacker, target, __, damageOverride: null);

        private void OnHit(NpcContainer attacker, NpcContainer target, Vector3 position, int? damageOverride,
            int overrideProtectionIndex = (int)DamageType.Fire)
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

            // Summons stop at a downed target like the scripts
            // (ZS_MM_Attack_Loop: C_NpcIsDown(other)) - a swing already under way doesn't finish it off.
            if (attacker.SummonedBy != null &&
                NpcHelperService.IsKnockedOut(target))
            {
                Logger.Log($"[FightService] {attacker.Instance.GetName(NpcNameSlot.Slot0)} (summon) doesn't finish off " +
                           $"unconscious {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
                return;
            }

            // Hero is being hit — alert party members immediately. We already know isHero here so no
            // comparison needed; attacker is directly the enemy party members should engage.
            if (isHero)
                AlertPartyMembersToDefendHero(attacker);

            // The melee hit lands (finishing off a downed NPC too).
            if (damageOverride == null && NpcHelperService.IsKnockedOut(target) && !isHero)
                GlobalEventDispatcher.MeleeHitLanded.Invoke(attacker, target, position);

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

            // DeveloperConfig.EnableDefeatedNpcFightsBack: a human the hero beat before is a coward towards him
            // (AIV_WASDEFEATEDBYSC: ZS_AssessEnemy -> ZS_ProclaimAndPunish -> ZS_TurnAway, "I didn't see anything") - fine
            // as a witness, but attacked himself he drew, put away and looked away over and over (his temporary
            // HOSTILE attitude restarts ZS_AssessEnemy through PERC_ASSESSENEMY). Hit by the hero, he fights back.
            var isHeroAttacker = attacker.Instance.IsHero(_gameStateService.GothicVm);
            // Not by resetting AIV_WASDEFEATEDBYSC: every knockout paid XP again and quests reading it changed (review
            // of PR #474). The coward fighting back needs a C# side flag - open.

            // The hero's hit on a parrying / jumping back NPC.
            if (!isHero && isHeroAttacker && TryBlockHit(attacker, target))
                return;

            // DeveloperConfig.EnableVrHeroBlock: a parry is its own state like the engine's - no hits while blocking.
            if (isHeroAttacker && damageOverride == null && _configService.Dev.EnableVrHeroBlock &&
                attacker.Props.IsBlocking)
            {
                Logger.Log($"[FightService] The hero blocks - his hit on {target.Instance.GetName(NpcNameSlot.Slot0)} " +
                           "doesn't count", LogCat.Fight);
                return;
            }

            // DeveloperConfig.EnableVrHeroBlock: an NPC's melee hit (no damage override: not a spell or projectile).
            if (isHero && damageOverride == null && TryHeroBlockHit(attacker, target, position))
                return;

            // Blood only now - not for a parried or blocked hit.
            if (damageOverride == null)
                GlobalEventDispatcher.MeleeHitLanded.Invoke(attacker, target, position);

            Logger.Log($"[FightService.OnHit] *** {attacker.Instance.GetName(NpcNameSlot.Slot0)} HIT " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
            if (_meleeDamageDomain.ApplyHit(attacker, target, damageOverride, overrideProtectionIndex))
            {
                // The hero goes down by the same rule as everybody (OpenGothic checkHealth): monsters and humans
                // hostile to him kill him, the others knock him out. A berzerk is always only stopped; arrows and
                // spells kill unless the "Ranged knockout" option is on (IsRangedKill). What the hero's summon does
                // counts as the hero's (GetCreditedAttacker).
                var isKnockedOut = isHero
                    ? _knockoutDomain.IsKnockout(attacker, target)
                    : !IsRangedKill(damageOverride) && target.IsHuman() &&
                      (IsBerzerk(target) || _knockoutDomain.IsKnockout(GetCreditedAttacker(attacker), target));
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
                    _knockoutDomain.KnockOutNpc(target, GetCreditedAttacker(attacker));
                }
                
                else
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is DEAD",
                        LogCat.Npc);
                    Kill(target, GetCreditedAttacker(attacker));
                }
            }
            else
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

            BroadcastDamagePerceptions(attacker, target);
        }

        /// Fire PERC_ASSESSDAMAGE on the target so it reacts (B_MM_ReactToDamage / B_AssessDamage),
        /// then broadcast PERC_ASSESSOTHERSDAMAGE to nearby NPCs so allies join the fight.
        /// <summary>
        /// The summoner hits its own summon (weapon, spell, arrow).
        /// </summary>
        private bool IsOwnSummonHit(NpcContainer attacker, NpcContainer target)
        {
            return target?.SummonedBy != null &&
                   attacker?.Instance != null && target.SummonedBy.Index == attacker.Instance.Index;
        }

        /// <summary>
        /// What the hero's summon does counts as the hero's - a knockout by
        /// it is the hero's win (ZS_Unconscious checks C_NpcIsHero(other): Buster's admission duel didn't go on after
        /// the hero's goblins beat him), witnesses see the hero as the attacker. Anyone else: the attacker itself.
        /// </summary>
        private NpcContainer GetCreditedAttacker(NpcContainer attacker)
        {
            if (attacker?.SummonedBy == null)
                return attacker;

            var hero = _npcService.GetHeroContainer();
            return hero?.Instance != null && attacker.SummonedBy.Index == hero.Instance.Index ? hero : attacker;
        }

        // HAI_DIST_HELPATTACKEDCHARGES-like: guards in this range step in.
        private const float _guardsHelpRange = 15f;

        private static bool IsBerzerk(NpcContainer npc)
        {
            return npc?.Vob != null && npc.Vob.CurrentStateName.EqualsIgnoreCase("ZS_Berzerk");
        }

        /// <summary>
        /// Guards near a berzerk NPC attack it to stop it - it is knocked out,
        /// not killed (it isn't hostile to them, so ZS_Attack_End doesn't finish it). Vanilla G1 let it rage: guards
        /// only protect charges from non-friends (B_AssessFightSound, "friendly fire"). G1 only (C_NpcIsGuard).
        /// </summary>
        private void CallGuardsAgainstBerzerk(NpcContainer berzerker)
        {
            if (!_configService.Dev.EnableGuardsAgainstBerzerk || !IsBerzerk(berzerker))
                return;

            var vm = _gameStateService.GothicVm;
            var attackState = vm.GetSymbolByName("ZS_Attack");
            if (attackState == null || vm.GetSymbolByName("C_NpcIsGuard") == null)
                return;

            var berzerkerPosition = berzerker.Go.transform.position;
            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            foreach (var guard in awakeNpcs)
            {
                if (guard == berzerker || guard.Props == null || guard.Go == null || guard.Vob == null ||
                    guard.PrefabProps == null || guard.PrefabProps.IsHero())
                    continue;
                if (guard.Props.BodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious)
                    continue;
                if (IsBerzerk(guard) || guard.Vob.CurrentStateName.EqualsIgnoreCase("ZS_Attack"))
                    continue;
                if ((guard.Go.transform.position - berzerkerPosition).sqrMagnitude > _guardsHelpRange * _guardsHelpRange)
                    continue;

                int isGuard;
                try
                {
                    isGuard = vm.Call<int, NpcInstance>("C_NpcIsGuard", guard.Instance);
                }
                catch (System.Exception e)
                {
                    Logger.LogWarning($"[FightService] C_NpcIsGuard failed: {e.Message}", LogCat.Fight);
                    return;
                }
                if (isGuard == 0)
                    continue;

                guard.Props.EnemyNpc = berzerker.Instance;
                guard.Props.TargetNpc = berzerker.Instance;

                var oldSelf = vm.GlobalSelf;
                var oldOther = vm.GlobalOther;
                vm.GlobalSelf = guard.Instance;
                vm.GlobalOther = berzerker.Instance;
                _npcAiService.ExtAiDrawWeapon(guard.Instance);
                _npcAiService.ExtAiStartState(guard.Instance, attackState.Index, false, "");
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;

                Logger.Log($"[FightService] {guard.Instance.GetName(NpcNameSlot.Slot0)} stops the berzerk " +
                           $"{berzerker.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
            }
        }

        private void BroadcastDamagePerceptions(NpcContainer attacker, NpcContainer target)
        {
            CallGuardsAgainstBerzerk(attacker);

            // A summon is a training dummy for its own summoner -
            // it takes the damage but neither it nor bystanders turn on the summoner.
            if (IsOwnSummonHit(attacker, target))
            {
                Logger.Log($"[FightService] {target.Instance.GetName(NpcNameSlot.Slot0)} ignores the hit of its summoner", LogCat.Fight);
                return;
            }

            // Skip on a target that just died from this hit: its registered AssessDamage handler
            // (e.g. ZS_MM_Attack, shared by monsters/summons) unconditionally calls AI_StandUp,
            // which resets BodyState back to BsStand and undoes the death we just set above.
            if (target.Props.BodyState != VmGothicEnums.BodyState.BsDead)
            {
                // Diagnostics (a berzerk victim didn't fight back): which damage reaction the victim has in its state.
                if (!target.Instance.IsHero(_gameStateService.GothicVm))
                {
                    var percFunction = target.Props.Perceptions.TryGetValue(VmGothicEnums.PerceptionType.AssessDamage,
                        out var percIndex) && percIndex >= 0
                        ? _gameStateService.GothicVm.GetSymbolByIndex(percIndex)?.Name ?? percIndex.ToString()
                        : "none";
                    Logger.Log($"[FightService.Perc] AssessDamage on {target.Instance.GetName(NpcNameSlot.Slot0)} " +
                               $"(state {target.Vob.CurrentStateName}) by " +
                               $"{attacker.Instance.GetName(NpcNameSlot.Slot0)}: {percFunction}", LogCat.Fight);
                }
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

            var heroContainer = _npcService.GetHeroContainer();
            var heroIndex = heroContainer.Instance.Index;

            // When a non-hero NPC hits a target that is alive, force-switch the target's focus to
            // the attacker. Gothic's B_CombatReactToDamage only does this for the player; we extend
            // it here so that wolves, monsters, etc. become the active fight target when they land
            // a hit. Still skip Friendly attackers (e.g. a stray combo hit in a crowd) so allies/guards
            // don't turn hostile on each other from incidental damage — same gate used elsewhere in
            // this file (ActivatePartyMemberAttack) and in NpcAiService's target-switching methods.
            // Same-guild is excluded unconditionally too — this mechanic has no vanilla precedent for
            // guild-mates fighting each other, so don't rely solely on the guild-attitude table for it.
            // Party members (summons/allies) are excluded too: an NPC being pummeled by the hero's golem
            // should still keep focusing the hero — the real aggressor — not redirect onto the summon
            // that's just piling on damage. This was the confirmed cause of NPCs abandoning the hero to
            // fight the party member instead (logs: "Strażnik bramy switches target to Golem").
            if (_configService.Dev.EnableNpcHitRetaliation &&
                attacker.Instance.Index != heroIndex && target.Props.BodyState != VmGothicEnums.BodyState.BsDead &&
                target.Instance.Guild != attacker.Instance.Guild &&
                !IsPartyMember(attacker) &&
                _npcAiService.ExtGetAttitude(target.Instance, attacker.Instance) != VmGothicEnums.Attitude.Friendly)
            {
                target.Props.TargetNpc = attacker.Instance;
                target.Props.EnemyNpc = attacker.Instance;
                Logger.Log($"[FightService] {target.Instance.GetName(NpcNameSlot.Slot0)} switches target to {attacker.Instance.GetName(NpcNameSlot.Slot0)} (hit by non-hero)", LogCat.Fight);
            }

            // Party member was hit by a non-hero — only defend itself once it has no alive primary
            // target to stay committed to (see HasAlivePrimaryTarget). Otherwise an incidental hit from
            // a bystander would pull it off whatever the hero is actually attacking.
            if (IsPartyMember(target) && attacker.Instance.Index != heroIndex && !HasAlivePrimaryTarget(target))
            {
                ActivatePartyMemberAttack(target, attacker);
                Logger.Log($"[FightService] Party member {target.Instance.GetName(NpcNameSlot.Slot0)} defends self against {attacker.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
            }

            if (attacker.Go == null) return;

            var creditedAttacker = GetCreditedAttacker(attacker);
            var broadcasted = 0;
            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            var awakeHero = _npcService.GetHeroContainer();
            for (var awakeIdx = -1; awakeIdx < awakeNpcs.Count; awakeIdx++)
            {
                var candidate = awakeIdx < 0 ? awakeHero : awakeNpcs[awakeIdx];
                if (candidate == null) continue;
                if (candidate == target || candidate == attacker)
                    continue;
                if (candidate.Props?.IsDown == true)
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
                    if (attacker.Instance.Index == heroIndex)
                    {
                        // Hero landed the hit — this is the "primary target" signal, always follow it.
                        enemy = target;
                        anchor = attacker;
                    }
                    else if (target.Instance.Index == heroIndex && !HasAlivePrimaryTarget(candidate))
                    {
                        // Hero got hit instead — only a fallback for "no target yet," not an override,
                        // so the party member doesn't ping-pong onto whoever last traded blows with the
                        // hero in a multi-enemy fight.
                        enemy = attacker;
                        anchor = target;
                    }

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
                // Party members excluded as well — a bystander that's lost sight of the hero should
                // keep trying to reacquire the hero, not settle on the summon that's visibly fighting
                // nearby instead.
                // Not when the attacker hits the hero himself - he's on the bystander's side (Rangar beat the hero,
                // the citizen fighting the hero turned on Rangar).
                if (attacker.Instance.Index != heroIndex && target.Instance.Index != heroIndex &&
                    candidate.Props.TargetNpc?.Index == heroContainer.Instance.Index &&
                    !_npcAiService.ExtNpcCanSeeNpc(candidate.Instance, heroContainer.Instance, false) &&
                    candidate.Instance.Guild != attacker.Instance.Guild &&
                    !IsPartyMember(attacker) &&
                    _npcAiService.ExtGetAttitude(candidate.Instance, attacker.Instance) != VmGothicEnums.Attitude.Friendly)
                {
                    candidate.Props.TargetNpc = attacker.Instance;
                    candidate.Props.EnemyNpc = attacker.Instance;
                    Logger.Log($"[FightService] {candidate.Instance.GetName(NpcNameSlot.Slot0)} switches focus from hidden hero to visible {attacker.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
                }

                // Bystanders blame the hero for his summon's hits - guards
                // go for him; the victim itself still fights back against the summon.
                var witnessedAttacker = creditedAttacker;
                if (candidate.Props.Perceptions.ContainsKey(VmGothicEnums.PerceptionType.AssessOthersDamage))
                {
                    _npcAiService.ExecutePerception(
                        VmGothicEnums.PerceptionType.AssessOthersDamage,
                        candidate.Props, candidate.Instance,
                        victim: target.Instance,
                        other: witnessedAttacker.Instance);
                }

                if (candidate.Props.Perceptions.ContainsKey(VmGothicEnums.PerceptionType.AssessFightSound))
                {
                    _npcAiService.ExecutePerception(
                        VmGothicEnums.PerceptionType.AssessFightSound,
                        candidate.Props, candidate.Instance,
                        victim: target.Instance,
                        other: witnessedAttacker.Instance);
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

        private bool IsPartyMember(NpcContainer npc)
        {
            var sym = _gameStateService.GothicVm.GetSymbolByName("AIV_MM_PARTYMEMBER");
            if (sym == null) return false;
            var aivIndex = sym.GetInt(0);
            return npc.Instance.GetAiVar(aivIndex) == 1;
        }

        // A party member's "primary target" is whatever the hero is actively attacking — sticky until
        // that target goes down, so a summon doesn't ping-pong between every enemy that trades hits
        // with the hero in a multi-enemy fight (logs showed exactly this: "Party member Golem → attack
        // Strażnik" / "→ attack Diego" alternating every few seconds as different guards traded blows
        // with the hero). Self-defense (reacting to whoever's hitting the party member directly) is
        // only allowed once there's no alive primary target to stay committed to.
        private bool HasAlivePrimaryTarget(NpcContainer partyMember)
        {
            var current = partyMember.Props.TargetNpc?.GetUserData();
            return current != null &&
                   current.Props.BodyState != VmGothicEnums.BodyState.BsDead &&
                   current.Props.BodyState != VmGothicEnums.BodyState.BsUnconscious;
        }

        private void AlertPartyMembersToDefendHero(NpcContainer attacker)
        {
            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            foreach (var candidate in awakeNpcs)
            {
                if (!IsPartyMember(candidate)) continue;
                if (NpcHelperService.IsDown(candidate)) continue;
                if (candidate.Go == null || !candidate.Go.activeInHierarchy) continue;

                // Fallback only — if the party member is already committed to a live primary target
                // (set by the hero's own attacks, see BroadcastDamagePerceptions), don't pull it off
                // that target just because a different enemy landed a hit on the hero.
                if (HasAlivePrimaryTarget(candidate)) continue;

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

        /// <summary>
        /// A human at 0 HP: knocked out or dead?
        /// Like the engine (OpenGothic Npc::checkHealth): a melee hit
        /// knocks out unless the victim is hostile to the attacker - permanent attitude towards the hero, guild
        /// attitude between NPCs. A guard no longer kills a citizen in a brawl, bandits still die. Arrows kill.
        /// A berzerk is always only stopped.
        /// </summary>
        /// <summary>
        /// DeveloperConfig.EnableRangedKnockoutOption: an arrow, bolt or spell (they pass their damage) kills unless the
        /// "rangedKnockout" option is on.
        /// </summary>
        private bool IsRangedKill(int? damageOverride)
        {
            return _configService.Dev.EnableRangedKnockoutOption && damageOverride.HasValue && !IsRangedKnockoutOn();
        }


        private void OnFinishingMove(NpcContainer attacker, NpcContainer target)
        {
            var isHero = target.PrefabProps != null && target.PrefabProps.IsHero();
            if (isHero && IsHeroDeathOn())
            {
                OnHeroDied(target, attacker);
                return;
            }
            if (isHero)
            {
                // No respawn system yet — treat as extended knockout so the player can reload.
                Logger.LogWarning("[FightService.FinishingMove] Hero executed — extended knockout (no respawn yet)", LogCat.Fight);
                if (target.Props.BodyState != VmGothicEnums.BodyState.BsUnconscious)
                    _knockoutDomain.KnockOutHero(target, attacker);
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
        /// DeveloperConfig.EnableHeroDeath + the "heroDeath" option (Immersion menu, on by default): an NPC finishing
        /// the knocked out hero off kills him like in Gothic. Off: he only stays knocked out.
        /// </summary>
        private bool IsHeroDeathOn()
        {
            return _configService.Dev.EnableHeroDeath &&
                   _configService.Gothic.GetInt(Constants.IniNames.HeroDeath, 1) == 1;
        }

        /// <summary>
        /// DeveloperConfig.EnableHeroDeath: like the engine (OpenGothic Npc::finishingMove - hit points 0, then
        /// PERC_ASSESSMURDER). The hero stays locked; the VR body falls dead and the menu opens (VRHeroBody).
        /// </summary>
        private void OnHeroDied(NpcContainer hero, NpcContainer attacker)
        {
            if (hero.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                return;

            Logger.LogWarning($"[FightService] Hero finished off by {attacker.Instance.GetName(NpcNameSlot.Slot0)} - " +
                              "DEAD", LogCat.Fight);
            _npcAiService.BroadcastDownPerception(hero, attacker, VmGothicEnums.PerceptionType.AssessMurder);
            // The same death as any other (EndGame: locked, the death scream, the menu offers loading only).
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






        /// <summary>
        /// DeveloperConfig.EnableRangedKnockoutOption + the "rangedKnockout" immersion option: off (default, like
        /// OpenGothic: only melee knocks out) a lethal arrow, bolt or spell kills a human; on, it knocks him out like a
        /// sword and the next shot, spell or fire rain finishes him off.
        /// </summary>
        private bool IsRangedKnockoutOn()
        {
            return _configService.Dev.EnableRangedKnockoutOption && _configService.Gothic != null &&
                   _configService.Gothic.GetInt(FightConst.RangedKnockoutIniName,
                       _configService.Dev.RangedKnockoutByDefault ? 1 : 0) == 1;
        }

        /// <summary>
        /// Area spells (fire rain, ice wave, a fire storm's flames) skip the dead - and the knocked out unless
        /// DeveloperConfig.EnableRangedKnockoutOption: like the engine they hit them too (finishing them off), the fire
        /// rain only ever knocked the Irdorath crew down.
        /// </summary>
        private bool IsSparedByAreaSpell(NpcContainer candidate)
        {
            if (!_configService.Dev.EnableRangedKnockoutOption)
                return NpcHelperService.IsDown(candidate);
            return candidate.Props.BodyState == VmGothicEnums.BodyState.BsDead;
        }

        // Set by CalculateMeleeDamage for the hit being calculated (OnHitUpdateHealth).
        private bool _isRequirementCapped;

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
            var mageTalent = vm.GetSymbolByName("NPC_TALENT_MAGE")?.GetInt(0) ?? _defaultMageTalent;
            var circle = _npcHelperService.ExtNpcGetTalentSkill(hero.Instance, mageTalent);
            if (circle >= rune.MagCircle)
                return true;

            if (isMessageShown && Time.time - _lastCannotUseMessageTime > _cannotUseMessageSeconds)
            {
                _lastCannotUseMessageTime = Time.time;
                Logger.Log($"[FightService] The hero knows circle {circle}, '{rune.Description}' needs {rune.MagCircle}",
                    LogCat.Fight);
                var function = vm.GetSymbolByName("G_CanNotCast");
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
            var function = vm.GetSymbolByName("G_CanNotUse");
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

        /// <summary>
        /// ZS_Dead of a corpse loaded from a save (AiHandler: dead on spawn) - its quest variables are set in the fresh
        /// VM (NpcDeathDomain).
        /// </summary>
        public void RunZsDead(NpcInstance dead, NpcInstance hero, NpcContainer killer)
        {
            _deathDomain.RunZsDead(dead, hero, killer);
        }

        private void OnDyingChangeAnimation(NpcContainer target)
        {
            // Dead state, AI queue dropped, animations stopped, physics off and the dying animation.
            _deathDomain.Die(target);
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
