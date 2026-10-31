using System;
using System.Collections.Generic;
using System.IO;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Material = UnityEngine.Material;
using Object = UnityEngine.Object;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// The look of spells, from the scripts' visual effects (VisualFxInst.d, OpenGothic GameScript::spellVfx):
    /// spellFX_[name] is the glow at the caster or the flying projectile, its emFXCollDyn_S effect shows where the spell
    /// hits. Only the effects' particles are shown - their meshes, lights, invest keys and sounds aren't yet, and the
    /// particle builder shows few of them - so a flying spell also gets a glowing core in the color of its damage.
    /// Owned by MagicService and WorldEffectService.
    /// </summary>
    public class SpellEffectDomain
    {
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly MeshService _meshService;

        private const float _coreDiameter = 0.15f;
        // DAM_INDEX_FIRE, DAM_INDEX_FLY, DAM_INDEX_MAGIC (C_Spell.damageType bits).
        private const int _damageFire = 1 << 3;
        private const int _damageFly = 1 << 4;
        private const int _damageMagic = 1 << 5;

        private readonly Dictionary<string, EffectBaseInstance> _effects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ParticleEffectEmitKeyInstance> _keys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Color, Material> _coreMaterials = new();
        private DaedalusVm _vfxVm;
        private bool _isVfxVmLoaded;

        /// <summary>
        /// The visual effect of that name (spellFX_Firebolt), null if the scripts have none.
        /// </summary>
        public EffectBaseInstance GetEffect(string name)
        {
            LoadVm();
            if (_vfxVm == null || string.IsNullOrEmpty(name))
                return null;
            if (_effects.TryGetValue(name, out var cached))
                return cached;

            EffectBaseInstance effect = null;
            if (_vfxVm.IsInstanceOf(_vfxVm.GetSymbolByName(name), "CFX_BASE"))
            {
                try
                {
                    effect = _vfxVm.InitInstance<EffectBaseInstance>(name);
                }
                catch (Exception e)
                {
                    Logger.LogWarning($"[Magic] Visual effect {name} failed: {e.Message}", LogCat.Fight);
                }
            }

            _effects[name] = effect;
            return effect;
        }

        /// <summary>
        /// A key of a visual effect (spellFX_Firebolt_KEY_CAST: what flies, what the cast creates), null without one.
        /// </summary>
        public ParticleEffectEmitKeyInstance GetKey(string name)
        {
            LoadVm();
            if (_vfxVm == null || string.IsNullOrEmpty(name))
                return null;
            if (_keys.TryGetValue(name, out var cached))
                return cached;

            ParticleEffectEmitKeyInstance key = null;
            if (_vfxVm.IsInstanceOf(_vfxVm.GetSymbolByName(name), "C_PARTICLEFXEMITKEY"))
            {
                try
                {
                    key = _vfxVm.InitInstance<ParticleEffectEmitKeyInstance>(name);
                }
                catch (Exception e)
                {
                    Logger.LogWarning($"[Magic] Visual effect key {name} failed: {e.Message}", LogCat.Fight);
                }
            }

            _keys[name] = key;
            return key;
        }

        /// <summary>
        /// The effect's particles at the position. Null if it shows none.
        /// </summary>
        /// <param name="lifetimeSeconds">They vanish after this long (looping ones too). 0 keeps them.</param>
        public GameObject CreateParticles(EffectBaseInstance effect, Vector3 position, Quaternion rotation,
            float lifetimeSeconds)
        {
            return CreateParticles(effect?.VisNameS, position, rotation, lifetimeSeconds);
        }

        /// <summary>
        /// The particles of that PFX at Gothic's own rate. Null if it isn't one (some effects show a mesh or texture).
        /// </summary>
        public GameObject CreateParticles(string pfxName, Vector3 position, Quaternion rotation, float lifetimeSeconds)
        {
            if (string.IsNullOrEmpty(pfxName))
                return null;

            pfxName = Path.GetFileNameWithoutExtension(pfxName);
            var pfxVm = _gameStateService.PfxVm;
            if (!pfxVm.IsInstanceOf(pfxVm?.GetSymbolByName(pfxName), "C_PARTICLEFX"))
                return null;

            var particles = _meshService.CreateVobPfx(pfxName, position, rotation,
                destroyAfterPlay: lifetimeSeconds > 0f, isFullRate: true);
            if (particles != null && lifetimeSeconds > 0f)
                Object.Destroy(particles, lifetimeSeconds);
            return particles;
        }

        /// <summary>
        /// The spell's effects spread a wave that touches NPCs (emCheckCollision), e.g. G1's fear: spellFX_Fear's cast key
        /// creates spellFX_Fear_Face, which creates spellFX_Fear_WAVE - whoever it touches perceives the spell.
        /// </summary>
        public bool HasCollidingWave(string spellName)
        {
            var effect = GetEffect(GetKey($"spellFX_{spellName}_KEY_CAST")?.EmCreateFxId);
            for (var depth = 0; effect != null && depth < 3; depth++)
            {
                if (effect.EmCheckCollision != 0)
                    return true;

                effect = GetEffect(effect.EmFxCreateS);
            }

            return false;
        }

        /// <summary>
        /// What shows where a spell hits: the effect's collision effect (emFXCollDyn_S on an NPC, emFXCollStat_S on a
        /// wall), else for an NPC the one of the effect its cast key creates (ice wave: the ice on the victim).
        /// </summary>
        public EffectBaseInstance GetImpactEffect(string spellName, bool isNpcHit)
        {
            var effect = GetEffect($"spellFX_{spellName}");
            var impact = GetEffect(isNpcHit ? effect?.EmFxCollDynS : effect?.EmFxCollStatS);
            if (impact != null || !isNpcHit)
                return impact;

            var created = GetEffect(GetKey($"spellFX_{spellName}_KEY_CAST")?.EmCreateFxId);
            return GetEffect(created?.EmFxCollDynS) ?? created;
        }

        /// <summary>
        /// A flying spell: the particles of its cast key (spellFX_[name]_KEY_CAST, what the engine flies - the effect's
        /// own particles are the glow in the hand), looping, and a glowing core (fire orange, wind white, other magic
        /// blue).
        /// </summary>
        public GameObject CreateProjectile(string spellName, SpellInstance spell, Vector3 position, Quaternion rotation)
        {
            var projectile = new GameObject($"Spell {spellName}");
            projectile.transform.SetPositionAndRotation(position, rotation);

            var castKey = GetKey($"spellFX_{spellName}_KEY_CAST");
            var pfxName = !string.IsNullOrEmpty(castKey?.VisNameS)
                ? castKey.VisNameS
                : GetEffect($"spellFX_{spellName}")?.VisNameS;
            var particles = CreateParticles(pfxName, position, rotation, 0f);
            if (particles != null)
            {
                particles.transform.SetParent(projectile.transform, true);
                foreach (var particleSystem in particles.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particleSystem.main;
                    main.loop = true;
                    particleSystem.Play();
                }
            }

            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            // Flights and the player's queries mustn't hit the projectile itself.
            Object.DestroyImmediate(core.GetComponent<Collider>());
            core.name = "Core";
            core.transform.SetParent(projectile.transform, false);
            core.transform.localScale = Vector3.one * _coreDiameter;
            core.GetComponent<MeshRenderer>().sharedMaterial = GetCoreMaterial(GetCoreColor(spell));
            return projectile;
        }

        private static Color GetCoreColor(SpellInstance spell)
        {
            var damageType = spell?.DamageType ?? 0;
            if ((damageType & _damageFire) != 0)
                return new Color(1f, 0.55f, 0.15f);
            if ((damageType & _damageFly) != 0)
                return new Color(0.9f, 0.9f, 0.9f);
            return new Color(0.45f, 0.6f, 1f);
        }

        private Material GetCoreMaterial(Color color)
        {
            if (_coreMaterials.TryGetValue(color, out var material) && material != null)
                return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            _coreMaterials[color] = material;
            return material;
        }

        private void LoadVm()
        {
            if (_isVfxVmLoaded)
                return;

            _isVfxVmLoaded = true;
            _vfxVm = _resourceCacheService.TryGetDaedalusVm("VISUALFX");
            if (_vfxVm == null)
                Logger.LogWarning("[Magic] VISUALFX scripts not found - spells have no particles.", LogCat.Fight);
        }
    }
}
