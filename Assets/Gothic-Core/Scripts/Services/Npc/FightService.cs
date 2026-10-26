using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc.Actions;
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
        [Inject] private readonly Gothic.Core.Services.Config.ConfigService _fightConfigService;
        [Inject] private readonly Gothic.Core.Services.Meshes.ParticleService _particleService;
        [Inject] private readonly Gothic.Core.Services.Player.PlayerService _playerService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly Gothic.Core.Services.Caches.VmCacheService _vmCacheService;
        [Inject] private readonly Gothic.Core.Services.Meshes.MeshService _meshService;

        // DAM_FIRE in Constants.d (bit after DAM_EDGE).
        private const int _damFire = 8;

        public void Init()
        {
            GlobalEventDispatcher.FightHit.AddListener(OnHit);
            GlobalEventDispatcher.SpellHit.AddListener(OnSpellHit);
            GlobalEventDispatcher.SpellSpreadHit.AddListener((caster, pos, damage) => ApplySpreadHit(caster, pos, damage, null));
            GlobalEventDispatcher.FallDamage.AddListener(OnFallDamage);
            GlobalEventDispatcher.CheatKill.AddListener(OnCheatKill);
            GlobalEventDispatcher.ScriptHitPointsDepleted.AddListener(OnScriptHitPointsDepleted);
            GlobalEventDispatcher.RangedHit.AddListener(OnRangedHit);
            GlobalEventDispatcher.FightFinishingMove.AddListener(OnFinishingMove);
            GlobalEventDispatcher.HeroMeleeSwing.AddListener(OnHeroMeleeSwing);
        }

        /// <summary>
        /// DeveloperConfig.EnableNpcPrehitReaction: the hero's swing is his "prehit" for a moment. NPCs fighting him
        /// stop a wait or strafe (the safe moves to cut short), so their next AI_Attack can parry or jump back - they
        /// pick a move only once per AI_Attack, and a VR swing is over before a waiting NPC would look again.
        /// </summary>
        private void OnHeroMeleeSwing(int weaponMaterial)
        {
            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null)
                return;
            hero.Props.MeleeSwingMaterial = weaponMaterial;

            if (!_configService.Dev.EnableNpcPrehitReaction)
                return;
            hero.Props.MeleePrehitUntil = Time.time + FightConst.HeroPrehitSeconds;

            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc == hero || npc?.Go == null || npc.Props == null || NpcHelperService.IsDown(npc))
                    continue;
                var enemy = npc.Props.EnemyNpc ?? npc.Props.TargetNpc;
                if (enemy == null || enemy.Index != hero.Instance.Index ||
                    Vector3.Distance(npc.Go.transform.position, hero.Go.transform.position) >
                    FightConst.HeroPrehitRangeMeters)
                    continue;

                // DeveloperConfig.EnableNpcPrehitCancelsAttack: an NPC in its own attack (combos of the tougher ones) never
                // looked at the hero's swings - its queued combo swings go, a swing before its hit frame stops.
                var queuedSwings = npc.Props.AnimationQueue.Count;
                var isOnlySwingsQueued = queuedSwings > 0 &&
                                         npc.Props.AnimationQueue.All(i => i is AttackPlayAni);
                if (queuedSwings > 0 && !(isOnlySwingsQueued && _configService.Dev.EnableNpcPrehitCancelsAttack))
                    continue;

                var interrupted = npc.Props.CurrentAction switch
                {
                    Wait wait => Interrupt(wait),
                    AttackPlayAni { Move: FightAiMove.Strafe } strafe => strafe.TryInterruptStrafe(),
                    AttackPlayAni attack when _configService.Dev.EnableNpcPrehitCancelsAttack =>
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
        /// DeveloperConfig.EnableNpcParadeBlocksHits: OpenGothic Npc::takeDamage - a parry facing the attacker
        /// (30 degrees) or a jump back (90 degrees) takes no damage, weapons clash (CS_IAI_ sound + CPFX_IAI_ effect).
        /// The whole parry/jump back animation counts (the engine: its DEF_WINDOW frames).
        /// </summary>
        private bool TryBlockHit(NpcContainer attacker, NpcContainer target)
        {
            if (!_configService.Dev.EnableNpcParadeBlocksHits || target.Go == null || attacker.Go == null ||
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
            if (!IsHuman(attacker) && attackerWeapon is not (VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H))
                return false;

            // DeveloperConfig.EnableBlockWeaponContact: only a hit that touched the held weapon is blocked - a block
            // held above the NPC's head blocked his hit too. The hit can reach the body first: it waits a moment for
            // the touch.
            if (_configService.Dev.EnableBlockWeaponContact)
            {
                if (HasBlockContact(attacker))
                {
                    BlockHit(attacker, hero);
                    return true;
                }
                _unityMonoService.StartCoroutine(AwaitBlockContact(attacker, hero, position));
                return true;
            }

            var forward = Vector3.ProjectOnPlane(hero.Props.BlockForward, Vector3.up);
            var toAttacker = Vector3.ProjectOnPlane(attacker.Go.transform.position - hero.Go.transform.position,
                Vector3.up);
            var angle = Vector3.Angle(forward, toAttacker);
            if (forward.sqrMagnitude < 0.0001f || angle > FightConst.HeroFocusAngle)
            {
                Logger.Log($"[FightService] {attacker.Instance.GetName(NpcNameSlot.Slot0)} hits the blocking hero " +
                           $"from the side ({angle:F0} degrees)", LogCat.Fight);
                return false;
            }

            BlockHit(attacker, hero);
            return true;
        }

        private const float _blockContactSeconds = 0.5f;
        private readonly Dictionary<NpcContainer, float> _blockContacts = new();
        // A deferred hit resolved without a block goes through OnHit again - without asking for a block twice.
        private bool _isBlockDecided;

        /// <summary>
        /// DeveloperConfig.EnableBlockWeaponContact: VR reports that an NPC's weapon (or fist) touched the hero's held
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
        /// DeveloperConfig.EnableNpcJumpAndFall: fall damage (NpcJumpFall). Like the engine a fall can kill.
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

            npc.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            OnDyingChangeAnimation(npc);
            OnNpcDied(npc, npc);
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
            npc.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            OnDyingChangeAnimation(npc);
            OnNpcDied(npc, npc);
        }

        /// <summary>
        /// DeveloperConfig.EnableScriptHitPointsDeath: a script took the NPC's last hit points (OpenGothic
        /// Npc::changeAttribute -> checkHealth). With the scripts' `other` as the attacker (B_MagicHurtNpc of a zapped
        /// victim: the caster) it's handled like script damage (death, or a knockout with the Ranged knockout option);
        /// an NPC weakening by itself (G1 summons in ZS_MM_SummonedByPC_Loop) just dies - nobody gets experience.
        /// </summary>
        private void OnScriptHitPointsDepleted(NpcContainer victim)
        {
            // Only an NPC standing in the world: the scripts' startup kills NPCs that aren't built yet (corpses) - they
            // spawn dead by their hit points; dying them here threw in the middle of the VM call and the loading hung.
            if (victim?.Props == null || victim.Go == null || victim.PrefabProps?.AnimationSystem == null ||
                NpcHelperService.IsDown(victim) || victim.PrefabProps.IsHero())
                return;

            var other = (_gameStateService.GothicVm?.GlobalOther as NpcInstance)?.GetUserData();
            if (other != null && other != victim && victim.SummonedBy == null)
            {
                OnScriptDamageDepleted(victim, other);
                return;
            }

            Logger.Log($"[FightService] {victim.Instance.GetName(NpcNameSlot.Slot0)} lost its last hit points by " +
                       "script - dies", LogCat.Fight);
            victim.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            OnDyingChangeAnimation(victim);
            OnNpcDied(victim, victim);
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

                foreach (var candidate in _multiTypeCacheService.NpcCache)
                {
                    if (candidate == caster || candidate.Go == null || !candidate.Go.activeInHierarchy)
                        continue;
                    if (IsSparedByAreaSpell(candidate))
                        continue;
                    if (Vector3.Distance(casterPos, candidate.Go.transform.position) > rangeM)
                        continue;

                    ApplySpellHit(caster, candidate, candidate.Go.transform.position, damage);
                    TryThrowBack(caster, candidate, mfxName, damage);
                    // DeveloperConfig.EnableSpellBodyFx: the spell's hit effect on everyone it reaches (ice wave: ice).
                    if (_fightConfigService.Dev.EnableSpellBodyFx)
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
        /// DeveloperConfig.EnableSpellSpreadDamage: the burning area of a fire storm (spellFX_Firestorm_SPREAD) damages
        /// everyone within SpellConst.SpreadRangeMeters of the impact - only the NPC hit directly took damage, one
        /// right next to the flames on the ground nothing.
        /// </summary>
        private void ApplySpreadHit(NpcContainer caster, Vector3 pos, int damage, NpcContainer directTarget)
        {
            if (!_fightConfigService.Dev.EnableSpellSpreadDamage || caster == null)
                return;

            var mfxName = GetSpellMfxName(caster.ActiveSpell);
            var hitCount = 0;
            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                if (candidate == caster || candidate == directTarget || candidate.Go == null ||
                    !candidate.Go.activeInHierarchy)
                    continue;
                if (IsSparedByAreaSpell(candidate))
                    continue;
                if (Vector3.Distance(pos, candidate.Go.transform.position) > SpellConst.SpreadRangeMeters)
                    continue;

                ApplySpellHit(caster, candidate, candidate.Go.transform.position, damage);
                if (_fightConfigService.Dev.EnableSpellBodyFx && mfxName != null)
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
                Gothic.Core.Adapters.Npc.HeroMagicState.Apply(target,
                    Gothic.Core.Adapters.Npc.HeroMagicState.GetSpellState(GetSpellMfxName(caster.ActiveSpell)));

            // Direct damage (Firebolt, Fireball, ...) still goes through the normal hit pipeline for
            // HP/animation/death handling. Reaction-only spells (damage=0) stop here — their effect
            // (freeze, sleep, ...) is entirely driven by the ASSESSMAGIC dispatch above, and routing
            // a fake 0-damage hit through OnHit would wrongly trigger pain animations/sounds.
            // DeveloperConfig.EnableEngineMeleeDamage: the spell's own damage type (OpenGothic: spl.damage_type) - every
            // spell was taken as fire (Windfist DAM_FLY, lightning DAM_MAGIC).
            var spellDamageType = _configService.Dev.EnableEngineMeleeDamage && caster.ActiveSpell >= 0
                ? _vmCacheService.TryGetSpellData(caster.ActiveSpell)?.DamageType ?? 0
                : 0;
            if (damage > 0)
                OnHit(caster, target, pos, damageOverride: damage,
                    overrideProtectionIndex: spellDamageType != 0
                        ? GetProtectionIndex(spellDamageType)
                        : (int)DamageType.Fire);

            // DeveloperConfig.EnableBurning: fire spells set the victim on fire (VOB_BURN + the s_Fire_Victim dance).
            // Not the hero - in VR there's no own body to burn.
            if (damage > 0 && _fightConfigService.Dev.EnableBurning && !target.Vob.Player && IsFireSpell(caster.ActiveSpell))
                Gothic.Core.Adapters.Npc.NpcBurnEffect.Ignite(target, _vmCacheService, _meshService, _audioService);
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
            Gothic.Core.Adapters.Npc.NpcJumpFall.Get(target)?.TryThrow(away.normalized * speed, _throwUpSpeed);
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

            if (IsHuman(victim) && IsRangedKnockoutOn() && IsKnockedOutNotKilled(attacker, victim))
            {
                Logger.Log($"[FightService] {victim.Instance.GetName(NpcNameSlot.Slot0)} zapped down - UNCONSCIOUS", LogCat.Fight);
                OnNpcKnockedOut(victim, attacker);
                return;
            }

            Logger.Log($"[FightService] {victim.Instance.GetName(NpcNameSlot.Slot0)} zapped to death", LogCat.Fight);
            victim.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            OnDyingChangeAnimation(victim);
            OnNpcDied(victim, attacker);
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

            if (OnHitUpdateHealth(hero, hero, damage, (int)DamageType.Magic))
            {
                Logger.LogWarning("[FightService] The hero zapped himself down", LogCat.Fight);
                OnHeroKnockedOut(hero, hero);
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

            var damage = weapon.DamageTotal;
            if (_contextGameVersionService.IsGothic2())
                damage += shooter.Vob.GetAttribute((int)NpcAttribute.Dexterity);
            else if (_configService.Dev.EnableRangedCriticalHits)
                damage = ApplyRangedCriticalHit(shooter, weapon, damage);
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

            // DeveloperConfig.EnableSummonActsForMaster: summons stop at a downed target like the scripts
            // (ZS_MM_Attack_Loop: C_NpcIsDown(other)) - a swing already under way doesn't finish it off.
            if (_configService.Dev.EnableSummonActsForMaster && attacker.SummonedBy != null &&
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

            // DeveloperConfig.EnableBloodOnlyOnHit: the melee hit lands (finishing off a downed NPC too).
            if (damageOverride == null && NpcHelperService.IsKnockedOut(target) && !isHero)
                GlobalEventDispatcher.MeleeHitLanded.Invoke(attacker, target, position);

            // Any attacker finishes off unconscious NPC — set dead and call ZS_Dead.
            if (NpcHelperService.IsKnockedOut(target) && !isHero)
            {
                Logger.Log($"[FightService] {attacker.Instance.GetName(NpcNameSlot.Slot0)} finishes off unconscious {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
                target.Props.BodyState = VmGothicEnums.BodyState.BsDead;
                OnDyingChangeAnimation(target);
                OnNpcDied(target, attacker);
                return;
            }

            // DeveloperConfig.EnableDefeatedNpcFightsBack: a human the hero beat before is a coward towards him
            // (AIV_WASDEFEATEDBYSC: ZS_AssessEnemy -> ZS_ProclaimAndPunish -> ZS_TurnAway, "I didn't see anything") - fine
            // as a witness, but attacked himself he drew, put away and looked away over and over (his temporary
            // HOSTILE attitude restarts ZS_AssessEnemy through PERC_ASSESSENEMY). Hit by the hero, he fights back.
            var isHeroAttacker = attacker.PrefabProps != null && attacker.PrefabProps.IsHero();
            if (!isHero && isHeroAttacker && _configService.Dev.EnableDefeatedNpcFightsBack && IsHuman(target))
            {
                var aivWasDefeated = _gameStateService.GothicVm.GetSymbolByName("AIV_WASDEFEATEDBYSC")?.GetInt(0) ?? -1;
                if (aivWasDefeated >= 0 && target.Instance.GetAiVar(aivWasDefeated) != 0)
                {
                    target.Instance.SetAiVar(aivWasDefeated, 0);
                    Logger.Log($"[FightService] {target.Instance.GetName(NpcNameSlot.Slot0)} was beaten by the hero before " +
                               "- attacked again, he fights back instead of looking away", LogCat.Fight);
                }
            }

            // DeveloperConfig.EnableNpcParadeBlocksHits: the hero's hit on a parrying / jumping back NPC.
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

            // DeveloperConfig.EnableBloodOnlyOnHit: blood only now - not for a parried or blocked hit.
            if (damageOverride == null)
                GlobalEventDispatcher.MeleeHitLanded.Invoke(attacker, target, position);

            Logger.Log($"[FightService.OnHit] *** {attacker.Instance.GetName(NpcNameSlot.Slot0)} HIT {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
            if (OnHitUpdateHealth(attacker, target, damageOverride, overrideProtectionIndex))
            {
                if (!isHero && NpcHelperService.IsKnockedOut(target))
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} finished off while unconscious — DEAD", LogCat.Npc);
                    target.Props.BodyState = VmGothicEnums.BodyState.BsDead;
                    OnDyingChangeAnimation(target);
                    OnNpcDied(target, attacker);
                }
                else if (isHero)
                {
                    Logger.LogWarning("[FightService] Hero knocked out!", LogCat.Fight);
                    OnHeroKnockedOut(target, attacker);
                }
                else if (IsHuman(target) && !IsRangedKill(damageOverride) &&
                         IsKnockedOutNotKilled(GetCreditedAttacker(attacker), target))
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is UNCONSCIOUS", LogCat.Npc);
                    OnNpcKnockedOut(target, GetCreditedAttacker(attacker));
                }
                
                else
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} is DEAD", LogCat.Npc);
                    target.Props.BodyState = VmGothicEnums.BodyState.BsDead;
                    OnDyingChangeAnimation(target);
                    OnNpcDied(target, GetCreditedAttacker(attacker));
                }
            }
            else
            {
                // DeveloperConfig.EnableNpcSwingKeepsOnNpcHit: an NPC's swing isn't cut short by another NPC's hit -
                // two bandits taking turns kept Cavalorn from ever landing one (stumble before his hit frame).
                // The hero's hits still interrupt like in Gothic.
                if (_configService.Dev.EnableNpcSwingKeepsOnNpcHit && !isHeroAttacker &&
                    target.Props.CurrentAction is AttackPlayAni { IsSwingInProgress: true })
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} took damage mid-swing - keeps swinging", LogCat.Npc);
                }
                else
                {
                    Logger.Log($"[FightService.OnHit] {target.Instance.GetName(NpcNameSlot.Slot0)} took damage, playing hurt animation", LogCat.Npc);
                    OnHitChangeAnimation(target);
                }
                OnHitPlaySound(target);
            }

            BroadcastDamagePerceptions(attacker, target);
        }

        /// Fire PERC_ASSESSDAMAGE on the target so it reacts (B_MM_ReactToDamage / B_AssessDamage),
        /// then broadcast PERC_ASSESSOTHERSDAMAGE to nearby NPCs so allies join the fight.
        /// <summary>
        /// DeveloperConfig.EnableSummonIgnoresMasterHits: the summoner hits its own summon (weapon, spell, arrow).
        /// </summary>
        private bool IsOwnSummonHit(NpcContainer attacker, NpcContainer target)
        {
            return _configService.Dev.EnableSummonIgnoresMasterHits && target?.SummonedBy != null &&
                   attacker?.Instance != null && target.SummonedBy.Index == attacker.Instance.Index;
        }

        /// <summary>
        /// DeveloperConfig.EnableSummonActsForMaster: what the hero's summon does counts as the hero's - a knockout by
        /// it is the hero's win (ZS_Unconscious checks C_NpcIsHero(other): Buster's admission duel didn't go on after
        /// the hero's goblins beat him), witnesses see the hero as the attacker. Anyone else: the attacker itself.
        /// </summary>
        private NpcContainer GetCreditedAttacker(NpcContainer attacker)
        {
            if (!_configService.Dev.EnableSummonActsForMaster || attacker?.SummonedBy == null)
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
        /// DeveloperConfig.EnableGuardsStopBerzerk: guards near a berzerk NPC attack it to stop it - it is knocked out,
        /// not killed (it isn't hostile to them, so ZS_Attack_End doesn't finish it). Vanilla G1 let it rage: guards
        /// only protect charges from non-friends (B_AssessFightSound, "friendly fire"). G1 only (C_NpcIsGuard).
        /// </summary>
        private void CallGuardsAgainstBerzerk(NpcContainer berzerker)
        {
            if (!_configService.Dev.EnableGuardsStopBerzerk || !IsBerzerk(berzerker))
                return;

            var vm = _gameStateService.GothicVm;
            var attackState = vm.GetSymbolByName("ZS_Attack");
            if (attackState == null || vm.GetSymbolByName("C_NpcIsGuard") == null)
                return;

            var berzerkerPosition = berzerker.Go.transform.position;
            foreach (var guard in _multiTypeCacheService.NpcCache)
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

            // DeveloperConfig.EnableSummonIgnoresMasterHits: a summon is a training dummy for its own summoner -
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
                if (!target.PrefabProps.IsHero())
                {
                    var percFunction = target.Props.Perceptions.TryGetValue(VmGothicEnums.PerceptionType.AssessDamage,
                        out var percIndex) && percIndex >= 0
                        ? _gameStateService.GothicVm.GetSymbolByIndex(percIndex)?.Name ?? percIndex.ToString()
                        : "none";
                    Logger.Log($"[FightService.Perc] AssessDamage on {target.Instance.GetName(NpcNameSlot.Slot0)} " +
                               $"(state {target.Vob.CurrentStateName}) by {attacker.Instance.GetName(NpcNameSlot.Slot0)}: {percFunction}",
                        LogCat.Fight);
                }
                // DeveloperConfig.EnableHitStopsWalking: like the engine's stumble, a hit interrupts walking - the
                // reaction (ZS_ReactToDamage: AI_StartState(ZS_AssessMonster)) queued behind a guide's long AI_GotoWP
                // and never ran: Baal Parvez stood still under a wolf's bites until spoken to.
                if (_configService.Dev.EnableHitStopsWalking && !target.PrefabProps.IsHero() &&
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
            if (attacker.Instance.Index != heroIndex && target.Props.BodyState != VmGothicEnums.BodyState.BsDead &&
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
            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                if (candidate == target || candidate == attacker)
                    continue;
                if (NpcHelperService.IsDown(candidate))
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

                // DeveloperConfig.EnableSummonActsForMaster: bystanders blame the hero for his summon's hits - guards
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
            foreach (var candidate in _multiTypeCacheService.NpcCache)
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
        /// DeveloperConfig.EnableEngineUnconsciousRule (like the engine, OpenGothic Npc::checkHealth): a melee hit
        /// knocks out unless the victim is hostile to the attacker - permanent attitude towards the hero, guild
        /// attitude between NPCs. A guard no longer kills a citizen in a brawl, bandits still die. Arrows kill.
        /// Off: only the hero and his party knock out. A berzerk is always only stopped (EnableGuardsStopBerzerk).
        /// </summary>
        /// <summary>
        /// DeveloperConfig.EnableRangedKnockoutOption: an arrow, bolt or spell (they pass their damage) kills unless the
        /// "rangedKnockout" option is on.
        /// </summary>
        private bool IsRangedKill(int? damageOverride)
        {
            return _configService.Dev.EnableRangedKnockoutOption && damageOverride.HasValue && !IsRangedKnockoutOn();
        }

        private bool IsKnockedOutNotKilled(NpcContainer attacker, NpcContainer target)
        {
            if (_configService.Dev.EnableGuardsStopBerzerk && IsBerzerk(target))
                return true;

            var isHeroAttacker = attacker.PrefabProps != null && attacker.PrefabProps.IsHero();
            if (!_configService.Dev.EnableEngineUnconsciousRule)
                return isHeroAttacker || IsPartyMember(attacker);

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
            DropReadiedWeapon(npc);

            npc.Vob.SetAttribute((int)NpcAttribute.HitPoints, 1);
            npc.Instance.SetAttribute(NpcAttribute.HitPoints, 1);

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
            // Not a script call: the old state's _End (e.g. ZS_ProclaimAndPunish_End) ran on the downed NPC and queued
            // weapon draws and new states - ZS_Unconscious never started and he reacted to everything while lying.
            _npcAiService.ExtAiStartState(npc.Instance, zsUnconscious.Index, true, "", false);
            npc.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;

            // DeveloperConfig.EnableKnockoutFallAnimation: the engine drops the NPC itself - ZS_Unconscious only calls
            // Mdl_ApplyRandomAni for a third of the tough guys, everybody else stood or T-posed while unconscious
            // (Buster) and still stood up normally afterwards.
            if (_configService.Dev.EnableKnockoutFallAnimation && npc.PrefabProps != null)
            {
                _physicsService.DisablePhysicsForNpc(npc.PrefabProps);
                npc.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(string0: "T_Stand_2_Wounded"), npc));
                npc.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(string0: "S_Wounded"), npc));
            }

            // DeveloperConfig.EnableSummonActsForMaster: summons fighting the NPC stop like the scripts
            // (ZS_MM_Attack_Loop ends on C_NpcIsDown) - their queued attacks kept hitting the downed NPC.
            if (_configService.Dev.EnableSummonActsForMaster)
            {
                foreach (var summon in _multiTypeCacheService.NpcCache)
                {
                    if (summon?.SummonedBy == null || summon.Props?.TargetNpc?.Index != npc.Instance.Index)
                        continue;
                    _npcAiService.ExtNpcClearAiQueue(summon.Instance);
                    Logger.Log($"[FightService] {summon.Instance.GetName(NpcNameSlot.Slot0)} (summon) stops attacking " +
                               $"downed {npc.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
                }
            }

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

            BroadcastDownPerception(npc, attacker, VmGothicEnums.PerceptionType.AssessDefeat);
        }

        private void OnHeroKnockedOut(NpcContainer hero, NpcContainer attacker)
        {
            hero.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;
            SetHasDefeatedHero(attacker);
            hero.Vob.SetAttribute((int)NpcAttribute.HitPoints, 1);
            hero.Instance.SetAttribute(NpcAttribute.HitPoints, 1);
            // DeveloperConfig.EnableKnockoutTurning: knocked out the hero can't walk, but can turn with the stick -
            // seated players couldn't look around otherwise.
            _contextInteractionService.LockPlayerInPlace(_configService.Dev.EnableKnockoutTurning);

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

            GlobalEventDispatcher.HeroKnockedOut.Invoke(hero);

            _unityMonoService.StartCoroutine(KnockoutRecovery(hero));
        }

        /// <summary>
        /// DeveloperConfig.EnableHeroDefeatedAivar: G1's ZS_Unconscious ends with "if (Npc_IsPlayer(self))
        /// other.aivar[AIV_HASDEFEATEDSC] = TRUE" - the hero runs no Daedalus states, so the NPC who knocked him out
        /// never knew it won. Dialogs after a lost duel check it (Mroczne Tajemnice's Sheer'Ghar: Info_Sheer_After).
        /// </summary>
        private void SetHasDefeatedHero(NpcContainer attacker)
        {
            if (!_configService.Dev.EnableHeroDefeatedAivar || attacker?.Instance == null ||
                (attacker.PrefabProps != null && attacker.PrefabProps.IsHero()))
                return;

            var aivSymbol = _gameStateService.GothicVm.GetSymbolByName("AIV_HASDEFEATEDSC");
            if (aivSymbol == null)
                return;

            attacker.Instance.SetAiVar(aivSymbol.GetInt(0), 1);
            Logger.Log($"[FightService] {attacker.Instance.GetName(NpcNameSlot.Slot0)} defeated the hero - " +
                       $"AIV_HASDEFEATEDSC ({aivSymbol.GetInt(0)}) = TRUE", LogCat.Fight);
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
                    OnHeroKnockedOut(target, attacker);
                return;
            }

            Logger.Log($"[FightService.FinishingMove] {attacker.Instance.GetName(NpcNameSlot.Slot0)} executes {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
            var wasLying = target.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious;
            target.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            // DeveloperConfig.EnableFinishedNpcLyingDeath: he dies where he lies, with his death scream.
            if (_configService.Dev.EnableFinishedNpcLyingDeath && wasLying && TryPlayLyingDeath(target))
                PlayDeathScream(target);
            else
                OnDyingChangeAnimation(target);
            OnNpcDied(target, attacker);
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

            hero.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            hero.Vob.SetAttribute((int)NpcAttribute.HitPoints, 0);
            hero.Instance.SetAttribute(NpcAttribute.HitPoints, 0);
            _contextInteractionService.LockPlayerInPlace(_configService.Dev.EnableKnockoutTurning);
            Logger.LogWarning($"[FightService] Hero finished off by {attacker.Instance.GetName(NpcNameSlot.Slot0)} - " +
                              "DEAD", LogCat.Fight);

            PlayDeathScream(hero);
            BroadcastDownPerception(hero, attacker, VmGothicEnums.PerceptionType.AssessMurder);
            GlobalEventDispatcher.HeroDied.Invoke(hero);
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

        // DeveloperConfig.EnableFinishedNpcLyingDeath: from lying unconscious (S_WOUNDED, on the belly; S_WOUNDEDB on
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

        private IEnumerator KnockoutRecovery(NpcContainer hero)
        {
            yield return new WaitForSeconds(10f);
            // Finished off in the meantime (EnableHeroDeath) - the dead don't stand up.
            if (hero.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                yield break;
            hero.Props.BodyState = VmGothicEnums.BodyState.BsStand;
            _contextInteractionService.UnlockPlayer();
            Logger.Log("[FightService] Hero recovered from knockout", LogCat.Fight);
        }

        /// <summary>
        /// Handles health changes.
        /// Returns true if Npc/Monster is dead.
        /// </summary>
        private int? _minimalDamage;

        private int GetMinimalDamage()
        {
            _minimalDamage ??= _gameStateService.GothicVm.GetSymbolByName("NPC_MINIMAL_DAMAGE")?.GetInt(0) ?? 0;
            return _minimalDamage.Value;
        }

        private bool OnHitUpdateHealth(NpcContainer attacker, NpcContainer target, int? damageOverride = null,
            int overrideProtectionIndex = (int)DamageType.Fire)
        {
            // FIXME - Talent/skill level (e.g. 1H skill) is not factored in yet.
            var hitPoints = target.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            var maxHP = target.Vob.GetAttribute((int)NpcAttribute.HitPointsMax);

            int damage;
            bool isImmune;
            if (damageOverride.HasValue)
            {
                // Spell/ranged damage bypasses the melee weapon formula — protection still applies.
                // FIXME: spells use their own damage type (overrideProtectionIndex stays Fire for them).
                var protection = target.Vob.GetProtection(overrideProtectionIndex);
                isImmune = protection < 0;
                damage = isImmune ? 0 : Mathf.Max(0, damageOverride.Value - protection);
            }
            else if (_configService.Dev.EnableEngineMeleeDamage)
            {
                damage = CalculateMeleeDamage(attacker, target, out isImmune);
            }
            else
            {
                var equippedWeapon = _npcHelperService.ExtNpcGetEquippedMeleeWeapon(attacker.Instance);
                // DeveloperConfig.EnableVrFists: in fist mode the engine hits with the fist - an equipped sword in the
                // belt doesn't add its damage (the VR hero punches with his weapon equipped).
                if (_fightConfigService.Dev.EnableVrFists &&
                    (VmGothicEnums.WeaponState)attacker.Vob.FightMode == VmGothicEnums.WeaponState.Fist)
                    equippedWeapon = null;

                // G1 melee damage: weapon damage + strength, reduced by the protection matching the damage type.
                // Unarmed attackers (fists, monster claws/bites) deal blunt damage with their strength alone.
                var strength = attacker.Vob.GetAttribute((int)NpcAttribute.Strength);
                var weaponDamage = equippedWeapon?.DamageTotal ?? 0;
                var protectionIndex = equippedWeapon == null
                    ? (int)DamageType.Blunt
                    : GetProtectionIndex(equippedWeapon.DamageType);
                var protection = target.Vob.GetProtection(protectionIndex);

                // Like G1: protection -1 means immune to this damage type; otherwise no damage when fully absorbed.
                isImmune = protection < 0;
                damage = isImmune ? 0 : Mathf.Max(0, weaponDamage + strength - protection);
            }

            // DeveloperConfig.EnableMinimalDamage: like the engine, a hit never does less than NPC_MINIMAL_DAMAGE
            // (Balancing.d, G1: 1) unless the target is immune (-1) - a meatbug (fire protection 100) took 0 from a
            // 50 damage fire storm and burned without losing HP.
            if (_configService.Dev.EnableMinimalDamage && !isImmune)
                damage = Mathf.Max(damage, GetMinimalDamage());

            // DeveloperConfig.EnableWeaponRequirementCap: 1 also against the minimal damage (Mroczne Tajemnice: 5).
            if (_isRequirementCapped && !isImmune)
                damage = 1;
            _isRequirementCapped = false;

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
            // DeveloperConfig.EnableRangedKnockoutOption replaces this (OnHit: kill or knock out by the option).
            var attackerMode = (VmGothicEnums.WeaponState)attacker.Vob.FightMode;
            var isMagicOrRanged = attackerMode is VmGothicEnums.WeaponState.Mage
                or VmGothicEnums.WeaponState.Bow
                or VmGothicEnums.WeaponState.CBow;
            var targetIsHumanOrHero = (target.PrefabProps != null && target.PrefabProps.IsHero()) || IsHuman(target);
            if (!_configService.Dev.EnableRangedKnockoutOption && isMagicOrRanged && targetIsHumanOrHero)
                damage = Mathf.Min(damage, Mathf.Max(1, hitPoints - 1));

            // NPC_FLAG_IMMORTAL (bit 1 = 2): take no damage, but combat still plays out normally.
            // Marvin's god mode for the hero likewise (the script flag is left alone - AI reads it).
            if (((int)target.Instance.Flags & 2) != 0 || IsHeroInGodMode(target))
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
            // DeveloperConfig.EnableDeathZeroHitPoints: dead is 0 hit points like the engine (OpenGothic
            // Npc::finishingMove / C_Kill: hit points -max) - a finished off NPC kept the knockout's 1 HP.
            if (_configService.Dev.EnableDeathZeroHitPoints &&
                dead.Vob.GetAttribute((int)NpcAttribute.HitPoints) > 0)
            {
                dead.Vob.SetAttribute((int)NpcAttribute.HitPoints, 0);
                dead.Instance.SetAttribute(NpcAttribute.HitPoints, 0);
                GlobalEventDispatcher.NpcAttributeChanged.Invoke(dead, NpcAttribute.HitPoints);
            }

            DropReadiedWeapon(dead);

            // Set flag immediately so AiHandler.Start() on the corpse doesn't double-call ZS_Dead.
            dead.IsZsDeadCalled = true;

            // ZS_Dead (B_CheckDeadMissionNPCs + B_GiveDeathInv etc.) takes ~280ms in G2 — too heavy
            // to run synchronously in the hit frame. Defer by one frame so the frame spike is gone.
            var deadInstance = dead.Instance;
            var heroInstance = _npcService.GetHeroContainer().Instance;
            _unityMonoService.StartCoroutine(CallZsDeadDeferred(deadInstance, heroInstance, killer));

            Logger.Log($"[FightService.OnNpcDied] {dead.Instance.GetName(NpcNameSlot.Slot0)} died — ZS_Dead deferred (killer: {killer.Instance.GetName(NpcNameSlot.Slot0)})", LogCat.Npc);
        }

        private bool IsXpForAnyKillOn()
        {
            return _configService.Gothic != null && _configService.Gothic.GetInt(FightConst.XpForAnyKillIniName, 0) == 1;
        }

        private IEnumerator CallZsDeadDeferred(NpcInstance dead, NpcInstance hero, NpcContainer killer)
        {
            yield return null;

            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = dead;
            // DeveloperConfig.EnableKillerXpOnly: like the engine (OpenGothic checkHealth: currentOther) 'other' is the
            // killer - ZS_Dead gives XP only if he's the hero or in his party (AIV_PARTYMEMBER, AIV_MM_PARTYMEMBER: the
            // hero's summons, Mordrag leading him), and G2 marks only the hero's victims as killed by him. The hero for
            // everybody gave XP for every death anywhere - now the "XP for every death" immersion option (a cheat).
            vm.GlobalOther = _configService.Dev.EnableKillerXpOnly && !IsXpForAnyKillOn()
                ? killer?.Instance ?? dead
                : hero;

            var zsDeadSym = vm.GetSymbolByName("ZS_Dead");
            if (zsDeadSym != null)
            {
                vm.Call(zsDeadSym.Index);

                // DeveloperConfig.EnableDeadStateLoop: the engine runs ZS_Dead_Loop too - G2 counts killed dragons
                // there (B_DragonKillCounter): the undead dragon's EXTRO video, the teleport to UNDEAD_ENDTELEPORT and
                // UndeadDragonIsDead never happened. The scripts guard it themselves (AIV_TAPOSITION), once is enough.
                var zsDeadLoopSym = _configService.Dev.EnableDeadStateLoop ? vm.GetSymbolByName("ZS_Dead_Loop") : null;
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
            else
                Logger.LogWarning("[FightService.OnNpcDied] ZS_Dead symbol not found", LogCat.Npc);

            vm.GlobalSelf = oldSelf;
            vm.GlobalOther = oldOther;

            BroadcastDownPerception(dead.GetUserData(), killer, VmGothicEnums.PerceptionType.AssessMurder);
        }

        /// <summary>
        /// DeveloperConfig.EnableMurderDefeatPerceptions: like the engine, witnesses around the victim learn who
        /// killed (PERC_ASSESSMURDER) or knocked out (PERC_ASSESSDEFEAT) whom: other = killer, victim = victim.
        /// The killer doesn't witness its own deed.
        /// </summary>
        private void BroadcastDownPerception(NpcContainer victim, NpcContainer killer, VmGothicEnums.PerceptionType perception)
        {
            if (!_configService.Dev.EnableMurderDefeatPerceptions || victim == null || killer == null || victim == killer)
                return;

            var notified = _npcAiService.BroadcastPassivePerception(victim, perception, victim.Instance,
                killer.Instance, exclude: killer);
            Logger.Log($"[FightService.Perc] {perception}: {killer.Instance.GetName(NpcNameSlot.Slot0)} -> " +
                       $"{victim.Instance.GetName(NpcNameSlot.Slot0)}, {notified} witness(es)", LogCat.Fight);
        }

        /// <summary>
        /// C_ITEM.damageType is a DAM_* bitmask whose bit positions match the PROT_* indices.
        /// Weapons carry one damage type; the first set bit wins.
        /// </summary>
        /// <summary>
        /// DeveloperConfig.EnableEngineMeleeDamage: OpenGothic DamageCalculator::swordDamage. Per damage type of the
        /// weapon (or the NPC's own damagetype: fists, claws - wolves bite DAM_EDGE): strength + the NPC's damage[] +
        /// the weapon's damage[] and damageTotal - the target's protection of that type; immune types (protection -1)
        /// are left out.
        /// G1: a critical hit with the talent value's chance (1H/2H) doubles the weapon part (DAM_CRITICAL_MULTIPLIER).
        /// G2: a miss by the hit chance (C_NPC.hitchance[1H/2H]) does a tenth; monsters without a weapon always hit.
        /// The hero's weapon is the one in his VR hand (Props.MeleeHitWeapon) - it isn't equipped in VR, he hit with
        /// his strength alone (Lukis in heavy armor took 30 instead of 80).
        /// DeveloperConfig.EnableWeaponRequirementCap (opinion): the hero swings a weapon without its requirements
        /// (cond_atr/cond_value, e.g. 50 strength) - every hit does only 1 damage.
        /// </summary>
        private int CalculateMeleeDamage(NpcContainer attacker, NpcContainer target, out bool isImmune)
        {
            var isHeroAttacker = attacker.PrefabProps != null && attacker.PrefabProps.IsHero();
            var weapon = isHeroAttacker
                ? attacker.Props.MeleeHitWeapon
                : _npcHelperService.ExtNpcGetEquippedMeleeWeapon(attacker.Instance);
            // DeveloperConfig.EnableVrFists: in fist mode the engine hits with the fist, not the sword in the belt.
            if (_fightConfigService.Dev.EnableVrFists &&
                (VmGothicEnums.WeaponState)attacker.Vob.FightMode == VmGothicEnums.WeaponState.Fist)
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

            if (isHeroAttacker && weapon != null && _configService.Dev.EnableWeaponRequirementCap &&
                !IsWeaponRequirementMet(attacker, weapon, out var requirement, out _, out _))
            {
                Logger.Log($"[FightService] The hero lacks {requirement} for '{weapon.Name}' - {damage} damage capped to 1",
                    LogCat.Fight);
                damage = Mathf.Min(damage, 1);
                _isRequirementCapped = true;
            }

            var rollResult = isG2 ? (isCritical ? "hit" : "near miss") : (isCritical ? "CRITICAL" : "normal");
            Logger.Log($"[FightService.MeleeDamage] {attacker.Instance.GetName(NpcNameSlot.Slot0)} -> " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)}: weapon '{weapon?.Name ?? "none"}' mask {mask}, " +
                       $"strength {strength}, {rollResult} (roll {roll}) = {damage}{(isImmune ? " (immune)" : "")}",
                LogCat.Fight);
            return damage;
        }

        /// <summary>
        /// DeveloperConfig.EnableRangedCriticalHits: G1 (OpenGothic Npc::shootBow + DamageCalculator::rangeDamage) - the
        /// bow/crossbow talent value is the chance of a critical hit (damage x DAM_CRITICAL_MULTIPLIER); dexterity is
        /// the hit chance, which a VR projectile doesn't need (it physically hit). G2 has no ranged critical hits: its
        /// hitchance[bow/crossbow] is the hit chance, dexterity adds to the damage.
        /// </summary>
        // DeveloperConfig.EnableModPercentTalents: Mroczne Tajemnice's teachers (B_GiveprocentSkill) keep a weapon's
        // percent in another talent's value - the one its status menu shows as the critical hit chance.
        private const string _modPercentSkillFunction = "B_GIVEPROCENTSKILL";
        private static readonly (string Weapon, string Percent)[] _modPercentTalents =
        {
            ("NPC_TALENT_1H", "NPC_TALENT_SNEAK"),
            ("NPC_TALENT_2H", "NPC_TALENT_REGENERATE"),
            ("NPC_TALENT_BOW", "NPC_TALENT_FIREMASTER"),
            ("NPC_TALENT_CROSSBOW", "NPC_TALENT_ACROBAT")
        };

        /// <summary>
        /// G1's critical hit chance: the weapon talent's value. DeveloperConfig.EnableModPercentTalents: with Mroczne
        /// Tajemnice's percent system the talent its teachers raise counts too (the higher one) - a hero trained by
        /// Cavalorn had his bow percent in NPC_TALENT_FIREMASTER and never hit critically.
        /// </summary>
        private int GetG1CriticalChance(NpcContainer npc, int talentId)
        {
            var chance = _npcHelperService.ExtNpcGetTalentValue(npc.Instance, talentId);
            var vm = _gameStateService.GothicVm;
            if (!_configService.Dev.EnableModPercentTalents || vm?.GetSymbolByName(_modPercentSkillFunction) == null)
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

        private int ApplyRangedCriticalHit(NpcContainer shooter, ItemInstance weapon, int damage)
        {
            var vm = _gameStateService.GothicVm;
            var isCrossbow = ((VmGothicEnums.ItemFlags)weapon.Flags & VmGothicEnums.ItemFlags.ItemCrossbow) != 0;
            var talentId = vm.GetSymbolByName(isCrossbow ? "NPC_TALENT_CROSSBOW" : "NPC_TALENT_BOW")?.GetInt(0) ??
                           (isCrossbow ? 4 : 3);
            var chance = GetG1CriticalChance(shooter, talentId);
            var roll = Random.Range(0, 100);
            if (chance <= roll)
                return damage;

            var multiplier = vm.GetSymbolByName("DAM_CRITICAL_MULTIPLIER")?.GetInt(0) ?? 2;
            Logger.Log($"[FightService.RangedHit] {shooter.Instance.GetName(NpcNameSlot.Slot0)}: CRITICAL " +
                       $"({chance} % > roll {roll}) - {damage} x {multiplier}", LogCat.Fight);
            return damage * multiplier;
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
        /// DeveloperConfig.EnableWeaponRequirementCap: does the hero meet a weapon's requirements (cond_atr/cond_value)?
        /// If not, like the engine's equip (OpenGothic Inventory::use -> printCannotUseError) the scripts' G_CanNotUse
        /// prints what is missing ("... strength points") - at most every few seconds. Melee weapons still hit (for 1),
        /// bows and crossbows don't shoot.
        /// </summary>
        public bool CheckHeroWeaponRequirement(ItemInstance weapon, bool isMessageShown)
        {
            if (!_configService.Dev.EnableWeaponRequirementCap || weapon == null)
                return true;

            var hero = _npcService.GetHeroContainer();
            if (hero == null || IsWeaponRequirementMet(hero, weapon, out var requirement, out var attribute, out var value))
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
        /// DeveloperConfig.EnableRuneCircleRequirement: does the hero know the rune's magic circle (C_ITEM.mag_circle vs
        /// NPC_TALENT_MAGE)? If not, like the engine the scripts' G_CanNotCast says how many circles are missing - at
        /// most every few seconds. Such a rune can't be equipped nor cast; scrolls have no circle.
        /// </summary>
        public bool CheckHeroRuneCircle(ItemInstance rune, bool isMessageShown)
        {
            if (!_configService.Dev.EnableRuneCircleRequirement || rune == null || rune.MagCircle <= 0)
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

        /// <summary>
        /// C_ITEM.cond_atr[]/cond_value[]: the attributes a weapon needs (G1/G2: strength or dexterity).
        /// </summary>
        private static bool IsWeaponRequirementMet(NpcContainer npc, ItemInstance weapon, out string requirement,
            out int missingAttribute, out int requiredValue)
        {
            requirement = null;
            missingAttribute = -1;
            requiredValue = 0;
            foreach (ItemConditionSlot slot in System.Enum.GetValues(typeof(ItemConditionSlot)))
            {
                var attribute = weapon.GetCondAtr(slot);
                var value = weapon.GetCondValue(slot);
                if (value <= 0 || attribute < 0 || attribute >= 8)
                    continue;
                if (npc.Vob.GetAttribute(attribute) >= value)
                    continue;

                requirement = $"{(NpcAttribute)attribute} {value} (has {npc.Vob.GetAttribute(attribute)})";
                missingAttribute = attribute;
                requiredValue = value;
                return false;
            }
            return true;
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

            // DeveloperConfig.EnableHeroHurtSound: the hero's pain sound like the NPCs' - from his own voice source (he
            // has no NpcSound, nothing was heard).
            if (_configService.Dev.EnableHeroHurtSound && target.PrefabProps != null && target.PrefabProps.IsHero())
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
