using System;
using System.Collections.Generic;
using Gothic.Core.Domain.Vobs;
using Gothic.Core.Extensions;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Meshes
{
    public class ParticleService
    {
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly IProjectileFactory _projectileFactory;
        [Inject] private readonly IVisualFxLightFactory _visualFxLightFactory;

        private const string _lightPresetsFile = "LIGHTPRESETS.ZEN";
        private const string _earthquakeVisual = "earthquake.eqk";
        // A one-shot effect's light dims this long before the effect is gone.
        private const float _fxLightFadeSeconds = 1f;
        private System.Collections.Generic.Dictionary<string, LightPresetLoader.LightPreset> _lightPresets;

        private const int _maxFxChainDepth = 3;
        private const string _defaultBloodEmitter = "PFX_BLOOD";
        // Gothic's 4 cm drops are hard to see in VR.
        private const float _bloodSizeScale = 2.5f;
        private const float _oneShotFxSeconds = 5f;
        private const float _npcSpellDefaultSpeed = 15f;
        private const float _npcSpellMaxSeconds = 4f;
        private const float _npcSpellHandHeight = 1.3f;
        private const float _npcSpellHandForward = 0.5f;
        private const float _npcSpellTargetHeight = 1f;
        // Fallback hit widening (emTrjTargetRange, 20 cm for the firebolt) if the spell's FX has none.
        private const float _npcSpellDefaultTargetRange = 0.2f;
        private const float _npcSpellFadeSeconds = 1f;

        public void Init()
        {
            // Blood when the hit landed - every swing that touched an NPC bled,
            // also a parried one or one on the blocking hero (sparks and blood together).
            GlobalEventDispatcher.MeleeHitLanded.AddListener(EmitBlood);
        }

        /// <summary>
        /// Plays a VISUALFX instance (spellFX_*, VOB_*): its PFX upright at the position (following `follow` if set),
        /// its sound and its emFXCreate_S chain. One-shot effects end on their own; looping ones (isLooping) stay
        /// until the caller destroys the returned roots. Its light preset glows with it, camera
        /// effects only play their sound (FX_EarthQuake).
        /// </summary>
        public List<GameObject> PlayVisualFx(string fxName, Vector3 position, Transform follow = null,
            bool isLooping = false, string skippedSfx = null, SkinnedMeshRenderer ownerMesh = null)
        {
            var created = new List<GameObject>();
            PlayVisualFx(fxName, position, follow, isLooping, skippedSfx, ownerMesh, created, 0);
            return created;
        }

        private void PlayVisualFx(string fxName, Vector3 position, Transform follow, bool isLooping, string skippedSfx,
            SkinnedMeshRenderer ownerMesh, List<GameObject> created, int depth)
        {
            if (string.IsNullOrEmpty(fxName) || depth > _maxFxChainDepth)
                return;
            if (fxName.StartsWith("FX_", StringComparison.OrdinalIgnoreCase))
            {
                PlayGlobalFx(fxName, position);
                return;
            }
            var fx = _vmCacheService.TryGetVfxData(fxName);
            if (fx == null)
            {
                Logger.LogWarning($"[VisualFx] {fxName} not found.", LogCat.Mesh);
                return;
            }

            if (!string.IsNullOrEmpty(fx.VisNameS))
            {
                var anchor = new GameObject($"VisualFx_{fxName}");
                anchor.transform.SetPositionAndRotation(position, Quaternion.identity);
                if (follow != null)
                    anchor.transform.SetParent(follow, true);
                var pfxGo = _meshService.CreateVobPfx(fx.VisNameS, parent: anchor, destroyAfterPlay: !isLooping,
                    isFullRate: true);
                if (pfxGo == null)
                {
                    UnityEngine.Object.Destroy(anchor);
                }
                else
                {
                    if (isLooping)
                    {
                        foreach (var particleSystem in anchor.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            var main = particleSystem.main;
                            main.loop = true;
                            particleSystem.Play();
                        }
                    }
                    else
                    {
                        // Until its last particle is gone (fire rain: 6.5 s of rain + 2.3 s falling - it was cut at 5 s).
                        UnityEngine.Object.Destroy(anchor, Mathf.Max(fx.EmFxLifespan, GetPlaySeconds(anchor)));
                    }
                    UseOwnerMesh(fx.VisNameS, anchor, ownerMesh);
                    AddLight(fx.LightPresetName, anchor, position, isLooping);
                    created.Add(anchor);
                }
            }

            if (!isLooping && !string.IsNullOrEmpty(fx.SfxId) &&
                !fx.SfxId.Equals(skippedSfx, StringComparison.OrdinalIgnoreCase))
            {
                var clip = _audioService.GetRandomSoundClip(fx.SfxId);
                if (clip != null)
                    AudioSource.PlayClipAtPoint(clip, position);
            }

            PlayVisualFx(fx.EmFxCreateS, position, follow, isLooping, skippedSfx, ownerMesh, created, depth + 1);
        }

        /// <summary>
        /// A light preset by name (LIGHTPRESETS.ZEN, read once).
        /// </summary>
        public LightPresetLoader.LightPreset TryGetLightPreset(string presetName)
        {
            if (string.IsNullOrEmpty(presetName))
                return null;
            if (_lightPresets == null)
            {
                _lightPresets = LightPresetLoader.Load(_resourceCacheService.Vfs.Find(_lightPresetsFile)?.Buffer.Bytes);
                Logger.Log($"[VisualFx] {_lightPresets.Count} light presets loaded", LogCat.Mesh);
            }
            if (_lightPresets.TryGetValue(presetName, out var preset))
                return preset;
            Logger.LogWarning($"[VisualFx] Unknown light preset {presetName}", LogCat.Mesh);
            return null;
        }

        /// <summary>
        /// A VISUALFX's light (OpenGothic Effect::setupLight) - rangeMeters is a
        /// key's lightRange, below 0 the preset's. Null without a preset.
        /// </summary>
        public IVisualFxLight CreateVisualFxLight(string presetName, Transform parent, Vector3 position,
            float rangeMeters = -1f)
        {
            var preset = TryGetLightPreset(presetName);
            return preset == null ? null : _visualFxLightFactory.Create(parent, position, preset, rangeMeters);
        }

        private void AddLight(string presetName, GameObject anchor, Vector3 position, bool isLooping)
        {
            var light = CreateVisualFxLight(presetName, anchor.transform, position);
            if (light == null || isLooping)
                return;
            var seconds = GetPlaySeconds(anchor);
            light.FadeOut(_fxLightFadeSeconds, Mathf.Max(0f, seconds - _fxLightFadeSeconds));
        }

        /// <summary>
        /// Camera effects (FX_*): FX_EarthQuake (earthquake.eqk) plays its sound and raises VisualFxEarthquake with
        /// its radius (userString[0], cm) and seconds (userString[1]) - no camera shake in VR, the controllers rumble.
        /// Time and screen effects (time.slw, screenblend.scx) are skipped.
        /// </summary>
        private void PlayGlobalFx(string fxName, Vector3 position)
        {
            var fx = _vmCacheService.TryGetVfxData(fxName);
            if (fx == null ||
                !_earthquakeVisual.Equals(fx.VisNameS, StringComparison.OrdinalIgnoreCase))
                return;

            var radius = fx.TryGetUserFloat(0, out var radiusCm)
                ? radiusCm / 100f
                : ZenGineConst.EarthquakeDefaultRadius;
            var seconds = fx.TryGetUserFloat(1, out var userSeconds)
                ? userSeconds
                : ZenGineConst.EarthquakeDefaultSeconds;
            if (!string.IsNullOrEmpty(fx.SfxId))
            {
                var clip = _audioService.GetRandomSoundClip(fx.SfxId);
                if (clip != null)
                    AudioSource.PlayClipAtPoint(clip, position);
            }
            Logger.Log($"[VisualFx] {fxName}: earthquake {radius:F0} m, {seconds:F1} s", LogCat.Mesh);
            GlobalEventDispatcher.VisualFxEarthquake.Invoke(position, radius, seconds);
        }

        private static float GetPlaySeconds(GameObject root)
        {
            var seconds = _oneShotFxSeconds;
            foreach (var particleSystem in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particleSystem.main;
                seconds = Mathf.Max(seconds, main.duration + main.startLifetime.constantMax);
            }
            return seconds;
        }

        /// <summary>
        /// shpType MESH: Gothic emits from the caster's body (teleport: the glowing silhouette) - the owner's skinned
        /// mesh if there is one.
        /// </summary>
        private void UseOwnerMesh(string pfxName, GameObject root, SkinnedMeshRenderer ownerMesh)
        {
            if (ownerMesh == null || !"MESH".Equals(_vmCacheService.TryGetPfxData(pfxName)?.ShpTypeS,
                    StringComparison.OrdinalIgnoreCase))
                return;

            foreach (var particleSystem in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var shape = particleSystem.shape;
                shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                shape.skinnedMeshRenderer = ownerMesh;
                shape.position = Vector3.zero;
                var main = particleSystem.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
            }
        }

        /// <summary>
        /// The hit effect of a spell on an NPC: emFXCollDyn_S of spellFX_<name>, or of the effect its CAST key creates
        /// (ice wave: spellFX_Icewave_WAVE -> spellFX_IceSpell_TARGET, the ice on everyone it hits).
        /// </summary>
        public void PlaySpellHitFx(string mfxName, NpcContainer target)
        {
            if (string.IsNullOrEmpty(mfxName) || target?.Go == null)
                return;

            var hitFx = _vmCacheService.TryGetVfxData($"spellFX_{mfxName}")?.EmFxCollDynS;
            if (string.IsNullOrEmpty(hitFx))
            {
                var castFx = _vmCacheService.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_CAST")?.EmCreateFxId;
                hitFx = string.IsNullOrEmpty(castFx) ? null : _vmCacheService.TryGetVfxData(castFx)?.EmFxCollDynS;
            }
            PlayVisualFx(hitFx, target.Go.transform.position, target.Go.transform);
        }

        /// <summary>
        /// What an NPC's spell looks like. A spell flying to its target (CAST key
        /// emTrjMode_S TARGET: fire ball, ice block, Mroczne Tajemnice's crawler web) flies there as its PFX on a
        /// Projectile (IProjectileFactory, like arrows) and hits on arrival (onHit). Other spells (sleep, summoning)
        /// show their cast effect at the caster and hit at once.
        /// Before NPC spells hit invisibly the moment the cast animation reached its frame.
        /// Returns false when nothing flies - the caller hits at once.
        /// </summary>
        public bool PlayNpcSpell(string mfxName, NpcContainer caster, NpcContainer target, Action onHit)
        {
            if (string.IsNullOrEmpty(mfxName) || caster?.Go == null || target?.Go == null)
                return false;

            var castKey = _vmCacheService.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_CAST");
            var baseFx = _vmCacheService.TryGetVfxData($"spellFX_{mfxName}");
            var isFlying = castKey != null && castKey.EmTrjEaseVel > 0f &&
                           castKey.EmTrjModeS.Contains("TARGET", StringComparison.OrdinalIgnoreCase);
            if (!isFlying)
            {
                PlayVisualFx(castKey?.EmCreateFxId, caster.Go.transform.position + Vector3.up * _npcSpellHandHeight);
                Logger.Log($"[NpcSpellFx] {mfxName}: no flight (key {(castKey == null ? "missing" : castKey.EmTrjModeS)})",
                    LogCat.Fight);
                return false;
            }

            var casterTransform = caster.Go.transform;
            var origin = casterTransform.position + Vector3.up * _npcSpellHandHeight +
                         casterTransform.forward * _npcSpellHandForward;
            var flightGo = new GameObject($"NpcSpell ({mfxName})");
            flightGo.transform.SetPositionAndRotation(origin, casterTransform.rotation);

            var pfxName = !string.IsNullOrEmpty(castKey.VisNameS) ? castKey.VisNameS
                : !string.IsNullOrEmpty(baseFx?.VisNameS) ? baseFx.VisNameS : $"MFX_{mfxName}_INIT";
            var pfxGo = _meshService.CreateVobPfx(pfxName, parent: flightGo, isFullRate: true);
            if (pfxGo == null)
            {
                Logger.LogWarning($"[NpcSpellFx] {mfxName}: PFX {pfxName} not found - hits at once", LogCat.Fight);
                UnityEngine.Object.Destroy(flightGo);
                return false;
            }
            foreach (var particleSystem in flightGo.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particleSystem.main;
                main.loop = true;
                particleSystem.Play();
            }

            var speed = castKey.EmTrjEaseVel / 100f > 0f ? castKey.EmTrjEaseVel / 100f : _npcSpellDefaultSpeed;
            var aimPoint = target.Go.transform.position + Vector3.up * _npcSpellTargetHeight;
            var projectile = _projectileFactory.Create(flightGo);
            projectile.Owner = caster;
            projectile.Velocity = (aimPoint - origin).normalized * speed;
            projectile.UseGravity = false;
            projectile.AllowAutoAim = false;
            // Like the engine (OpenGothic World::shootSpell): aimed once at the target, straight flight, no homing - the
            // hit test is widened by the effect's emTrjTargetRange (cm).
            var targetRange = baseFx?.EmTrjTargetRange ?? 0f;
            projectile.Radius = targetRange > 0f ? targetRange / 100f : _npcSpellDefaultTargetRange;
            projectile.MaxDistance = speed * _npcSpellMaxSeconds;
            projectile.OnNpcHit = (npc, _, _) =>
            {
                // Like the engine, it stops at the first body or wall; only its target is hit (SpellHitDomain).
                if (npc == target)
                {
                    onHit?.Invoke();
                    // Area spells play it on everyone they reach (FightService).
                    if (!SpellConst.AoeEffectNames.Contains(mfxName))
                        PlaySpellHitFx(mfxName, target);
                }
                FadeNpcSpell(flightGo);
            };
            projectile.OnWorldHit = (_, _) => FadeNpcSpell(flightGo);
            projectile.OnExpired = () => FadeNpcSpell(flightGo);

            Logger.Log($"[NpcSpellFx] {caster.Instance.GetName(ZenKit.Daedalus.NpcNameSlot.Slot0)} casts {mfxName} " +
                       $"({pfxName}, {speed:F1} m/s) at {target.Instance.GetName(ZenKit.Daedalus.NpcNameSlot.Slot0)}",
                LogCat.Fight);
            return true;
        }

        /// <summary>
        /// The spell's flight ended (hit, wall, too far): its particles stop emitting and fade out.
        /// </summary>
        private void FadeNpcSpell(GameObject flightGo)
        {
            if (flightGo == null)
                return;

            foreach (var particles in flightGo.GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            UnityEngine.Object.Destroy(flightGo, _npcSpellFadeSeconds);
        }

        private void EmitBlood(NpcContainer _, NpcContainer target, Vector3 position)
        {
            if (target == null || target.Instance == null)
                return;

            // Resolve guild-specific blood data
            var guild = target.Instance.Guild;

            // Emitter string used by MeshBuilder/PFX setup
            var emitter = _gameStateService?.GuildValues?.GetBloodEmitter(guild);
            // G1 sets BLOOD_EMITTER only for demons, golems, meatbugs, molerats - everyone else uses the engine default.
            if (emitter.IsNullOrEmpty())
                emitter = _defaultBloodEmitter;

            // Create particle effect at the hit position
            var bloodGo = _meshService.CreateVobPfx(emitter, position, Quaternion.identity, parent: target.Go,
                destroyAfterPlay: true);
            if (bloodGo == null)
                return;
            foreach (var particleSystem in bloodGo.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particleSystem.main;
                main.startSizeMultiplier *= _bloodSizeScale;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
            }
        }
    }
}
