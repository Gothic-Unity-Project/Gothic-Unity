using System;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// An NPC casting its spell at its target (CastNpcSpell: Spell_ProcessMana, cast sound, the flying effect) and a
    /// spell reaching NPCs: the victim's PERC_ASSESSMAGIC, the hit with the spell's damage type, area spells (AOE
    /// around the caster, a fire storm's burning spread), fire setting the victim on fire, Windfist throwing it away.
    /// The hit itself goes through FightService (applyHit: attacker, target, position, damage, protection index).
    /// </summary>
    public class SpellHitDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;
        [Inject] private readonly ParticleService _particleService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly INpcJumpFallFactory _npcJumpFallFactory;
        [Inject] private readonly AudioService _audioService;

        // DAM_FIRE in Constants.d (bit after DAM_EDGE).
        private const int _damFire = 8;
        private const string _chainLightningName = "Chainlightning";
        private const float _throwBaseSpeed = 3f;
        private const float _throwMaxSpeed = 8f;
        private const float _throwDamagePerSpeed = 25f;
        private const float _throwUpSpeed = 3.5f;
        // For spells with no dedicated cast sound.
        private const string _fallbackCastSoundName = "MFX_Thunderbolt_Cast";

        private readonly Action<NpcContainer, NpcContainer, Vector3, int, int> _applyHit;


        public SpellHitDomain(Action<NpcContainer, NpcContainer, Vector3, int, int> applyHit)
        {
            _applyHit = applyHit;
        }

        /// <summary>
        /// An NPC's spell at its hit frame (AttackPlayAni): its active spell at its level, like for the hero
        /// (VRRuneCaster). Spell_ProcessMana runs so content-side effects (e.g. Spell_Logic_SummonSkeleton ->
        /// Wld_SpawnNpcRange) fire for NPC casters too. A spell that flies hits (SpellHit) when it arrives.
        /// </summary>
        public void CastNpcSpell(NpcContainer caster, NpcContainer target, Vector3 targetPos)
        {
            var mfxName = GetSpellMfxName(caster.ActiveSpell);
            var spellDamage = _vmCacheService.GetSpellDamagePerLevel(caster.ActiveSpell, mfxName) *
                              caster.ActiveSpellLevel;
            PlaySpellCastSound(caster, mfxName);
            CallSpellProcessMana(caster, target);

            // SpellHit both applies direct damage (if any) and fires PERC_ASSESSMAGIC on the
            // target (always the enemy here, since AttackPlayAni only casts for active fight moves),
            // so Daedalus content (ZS_MagicFreeze, ZS_MagicSleep, ZS_Zapped, Fear/Charm/Berzerk...)
            // drives the actual reaction — nothing per-spell to hardcode here.
            Logger.Log($"[SpellHitDomain] NPC spell hit: ActiveSpell={caster.ActiveSpell} " +
                       $"level={caster.ActiveSpellLevel} damage={spellDamage}", LogCat.Fight);
            void Hit() => GlobalEventDispatcher.SpellHit.Invoke(caster, target,
                target.Go != null ? target.Go.transform.position : targetPos, spellDamage);
            if (!_particleService.PlayNpcSpell(mfxName, caster, target, Hit))
            {
                Hit();
                if (!SpellConst.AoeEffectNames.Contains(mfxName ?? ""))
                    _particleService.PlaySpellHitFx(mfxName, target);
            }
        }

        private void CallSpellProcessMana(NpcContainer caster, NpcContainer target)
        {
            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = caster.Instance;
            vm.GlobalOther = target.Instance;
            try
            {
                vm.Call<int, int>("Spell_ProcessMana", caster.ActiveSpellLevel);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
            }
        }

        private void PlaySpellCastSound(NpcContainer caster, string mfxName)
        {
            if (string.IsNullOrEmpty(mfxName))
                return;
            var clip = _audioService.GetRandomSoundClip($"MFX_{mfxName}_Cast");
            if (clip == null)
                clip = _audioService.GetRandomSoundClip(_fallbackCastSoundName);
            if (clip == null || caster.PrefabProps.NpcSound == null)
                return;
            caster.PrefabProps.NpcSound.PlayOneShot(clip);
        }

        /// <summary>
        /// The caster's active spell hit target at pos - an area spell hits everyone around the caster instead.
        /// </summary>
        public void OnSpellHit(NpcContainer caster, NpcContainer target, Vector3 pos, int damage)
        {
            var mfxName = GetSpellMfxName(caster.ActiveSpell);

            // DeveloperConfig.EnableVrChainLightning: chain lightning zaps its victims one by one (VRChainLightning, the
            // scripts' ZS_Zapped does the damage) - not everyone around the caster at once.
            var isChainLightning = _configService.Dev.EnableVrChainLightning &&
                                   string.Equals(mfxName, _chainLightningName, StringComparison.OrdinalIgnoreCase);
            if (mfxName != null && SpellConst.AoeEffectNames.Contains(mfxName) && !isChainLightning)
            {
                ApplyAreaHit(caster, mfxName, damage);
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
        public void ApplySpreadHit(NpcContainer caster, Vector3 pos, int damage, NpcContainer directTarget)
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
                if (NpcHelperService.IsDown(candidate))
                    continue;
                if (Vector3.Distance(pos, candidate.Go.transform.position) > SpellConst.SpreadRangeMeters)
                    continue;

                ApplySpellHit(caster, candidate, candidate.Go.transform.position, damage);
                if (mfxName != null)
                    _particleService.PlaySpellHitFx(mfxName, candidate);
                hitCount++;
            }

            Logger.Log($"[SpellHitDomain] {caster.Instance.GetName(NpcNameSlot.Slot0)}'s '{mfxName}' spread area " +
                       $"({SpellConst.SpreadRangeMeters:F0} m) hit {hitCount} more NPC(s), dmg={damage}", LogCat.Fight);
        }

        /// <summary>
        /// An area spell (fire rain, ice wave, ...) hits everyone awake within its range around the caster.
        /// </summary>
        private void ApplyAreaHit(NpcContainer caster, string mfxName, int damage)
        {
            var rangeM = GetAoeRangeMeters(mfxName);
            var casterPos = caster.Go.transform.position;
            Logger.Log($"[SpellHitDomain] {caster.Instance.GetName(NpcNameSlot.Slot0)} casts AOE '{mfxName}' " +
                       $"(range={rangeM:F1}m) dmg={damage}", LogCat.Fight);

            var awakeNpcs = _npcMeshCullingService.AwakeNpcs;
            var awakeHero = _npcService.GetHeroContainer();
            for (var awakeIdx = -1; awakeIdx < awakeNpcs.Count; awakeIdx++)
            {
                var candidate = awakeIdx < 0 ? awakeHero : awakeNpcs[awakeIdx];
                if (candidate == null) continue;
                if (candidate == caster || candidate.Go == null || !candidate.Go.activeInHierarchy)
                    continue;
                if (NpcHelperService.IsDown(candidate))
                    continue;
                if (Vector3.Distance(casterPos, candidate.Go.transform.position) > rangeM)
                    continue;

                ApplySpellHit(caster, candidate, candidate.Go.transform.position, damage);
                TryThrowBack(caster, candidate, mfxName, damage);
                // The spell's hit effect on everyone it reaches (ice wave: ice).
                _particleService.PlaySpellHitFx(mfxName, candidate);
            }
        }

        private void ApplySpellHit(NpcContainer caster, NpcContainer target, Vector3 pos, int damage)
        {
            // Like the engine (OpenGothic takeDamage: isDown) a spell does nothing to the dead and the knocked out - no
            // damage, no PERC_ASSESSMAGIC, no fire. Only a finishing move kills a knocked out NPC.
            if (NpcHelperService.IsDown(target))
            {
                Logger.Log($"[SpellHitDomain] {target.Instance.GetName(NpcNameSlot.Slot0)} is down - the spell of " +
                           $"{caster.Instance.GetName(NpcNameSlot.Slot0)} does nothing", LogCat.Fight);
                return;
            }

            Logger.Log($"[SpellHitDomain] {caster.Instance.GetName(NpcNameSlot.Slot0)} → " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)} dmg={damage}", LogCat.Fight);

            // PERC_ASSESSMAGIC drives victim-side spell reactions (B_AssessMagic -> ZS_MagicFreeze,
            // ZS_MagicSleep, ZS_Zapped, Fear/Charm/Berzerk, guild immunity checks, ally fight-sound).
            // That's pure Daedalus content dispatched by spell ID, so it covers every "bad" spell
            // (Sleep, Icecube, Fear, Charm, Berzerk, ChainLightning...) without any C# per-spell
            // logic, and works identically for G1/G2 since it's just a symbol lookup by name.
            if (!DamagePerceptionDomain.IsOwnSummonHit(caster, target))
            {
                _npcAiService.ExecutePerception(
                    VmGothicEnums.PerceptionType.AssessMagic,
                    target.Props, target.Instance,
                    victim: target.Instance,
                    other: caster.Instance);
            }

            // The hero has no PERC_ASSESSMAGIC - sleep/freeze are set here so the
            // caster's scripts see them (Npc_IsInState) instead of casting again and again.
            if (target.Vob.Player)
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
                _applyHit(caster, target, pos, damage, spellDamageType != 0
                    ? MeleeDamageDomain.GetProtectionIndex(spellDamageType)
                    : (int)DamageType.Fire);

            // Fire spells set the victim on fire (VOB_BURN + the s_Fire_Victim dance).
            // Not the hero - in VR there's no own body to burn.
            if (damage > 0 && !target.Vob.Player && IsFireSpell(caster.ActiveSpell))
                GlobalEventDispatcher.NpcIgnited.Invoke(target);
        }

        /// <summary>
        /// Windfist and Stormfist (DAM_FLY) throw the living away from the caster -
        /// the harder the spell, the farther (NpcJumpFall.TryThrow). Not the hero (his body is the player's).
        /// </summary>
        private void TryThrowBack(NpcContainer caster, NpcContainer target, string mfxName, int damage)
        {
            if (mfxName == null || caster?.Go == null || target?.Go == null ||
                target == caster || (target.PrefabProps != null && target.PrefabProps.IsHero()) ||
                NpcHelperService.IsDown(target) || !SpellConst.ThrowEffectNames.Contains(mfxName))
                return;

            var away = Vector3.ProjectOnPlane(target.Go.transform.position - caster.Go.transform.position, Vector3.up);
            if (away.sqrMagnitude < 0.0001f)
                away = caster.Go.transform.forward;
            var speed = Mathf.Clamp(_throwBaseSpeed + damage / _throwDamagePerSpeed, _throwBaseSpeed, _throwMaxSpeed);
            _npcJumpFallFactory.Get(target)?.TryThrow(away.normalized * speed, _throwUpSpeed);
        }

        private bool IsFireSpell(int spellId)
        {
            var damageType = spellId < 0 ? 0 : _vmCacheService.TryGetSpellData(spellId)?.DamageType ?? 0;
            return (damageType & _damFire) != 0;
        }

        private string GetSpellMfxName(int spellId)
        {
            return _vmCacheService.TryGetSpellMfxName(spellId, out var name) ? name : null;
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
    }
}
