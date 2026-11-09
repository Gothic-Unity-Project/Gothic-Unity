using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// What a hero's spell looks and sounds like in VR (VISUALFX of spellFX_[name]): the effect in the hand, the
    /// charging effects and light at the body, the cast effects and light, collision effects, the invest/cast sounds
    /// and the Light spell. Worker of VRRuneCaster (new VRSpellFx(...).Inject(), Dispose on destroy) - it decides
    /// when, this class how.
    /// </summary>
    public class VRSpellFx
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly ParticleService _particleService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly StationaryLightsService _stationaryLightsService;

        // Gothic's invest levels (spellFX_[name]_KEY_INVEST_1..4).
        public const int MaxInvestLevel = 4;

        private const float _bodyFxHeight = 1f; // BIP01 above the feet
        private const float _bodyFxFadeSeconds = 2f;
        // A body effect with a downward offset ends this far above the ground.
        private const float _groundFxClearance = 0.05f;
        private const float _castLightSeconds = 0.3f;
        private const float _castLightFadeSeconds = 1.5f;
        private const float _collideFxSeconds = 3f;

        private const float _lightRange = 6f;
        private const float _lightIntensity = 1.5f;
        private const float _lightDurationSeconds = 90f;
        private static readonly Color _lightColor = new(1f, 0.85f, 0.6f);

        private readonly GameObject _owner;
        private readonly string _mfxName;
        private readonly AudioSource _investAudioSource;

        // VISUALFX effects at the body/hand while charging, and the light while charging.
        private readonly List<GameObject> _bodyFxGos = new();
        private IVisualFxLight _investLight;
        private int _bodyInvestLevel;
        private GameObject _handVfxGo;
        // Cached on first use: the VR body (shpType MESH effects).
        private VRHeroBody _heroBody;

        /// <summary>
        /// The hand the spell comes from: the throwing hand of a throwable spell, otherwise the rune hand.
        /// </summary>
        public VrHandSide HandSide { get; set; }

        /// <summary>
        /// The invest level of a spell charged at the body (1 after SpawnInvestFx, +1 per NextBodyInvestLevel).
        /// </summary>
        public int BodyInvestLevel => _bodyInvestLevel;

        /// <summary>
        /// The effect in the hand (null if there is none).
        /// </summary>
        public GameObject HandVfx => _handVfxGo;

        /// <summary>
        /// owner: the rune/scroll (invest sound source, cast sound position). mfxName: spellFXInstanceNames entry.
        /// </summary>
        public VRSpellFx(GameObject owner, string mfxName)
        {
            _owner = owner;
            _mfxName = mfxName;
            _investAudioSource = owner.AddComponent<AudioSource>();
            _investAudioSource.loop = true;
            _investAudioSource.spatialBlend = 1f;
        }

        /// <summary>
        /// The rune is gone: every effect and sound it started ends.
        /// </summary>
        public void Dispose()
        {
            DestroyHandVfx();
            DestroyBodyFx();
            if (_investAudioSource == null)
                return;
            StopInvestSound();
            Object.Destroy(_investAudioSource);
        }

        /// <summary>
        /// The effect in the hand leaves it (a thrown spell flies with it) - null if there is none.
        /// </summary>
        public GameObject TakeHandVfx()
        {
            var handVfx = _handVfxGo;
            _handVfxGo = null;
            return handVfx;
        }

        /// <summary>
        /// The charge sound of an invest level loops while charging (sfxIsAmbient).
        /// </summary>
        public void PlayInvestLevelSound(string sfxId)
        {
            if (string.IsNullOrEmpty(sfxId))
                return;
            var clip = _audioService.GetRandomSoundClip(sfxId);
            if (clip == null)
                return;
            _investAudioSource.clip = clip;
            _investAudioSource.Play();
        }

        public void SpawnHandVfx(VrHandSide handSide, string pfxNameOverride = null)
        {
            DestroyHandVfx();
            var mfxName = _mfxName;
            var handGo = _vrPlayerService.GetHandModelGo(handSide);
            if (mfxName == null || handGo == null)
            {
                Logger.LogWarning($"[VRSpellFx] No effect in the hand " +
                                  $"(effect name: {mfxName ?? "-"}, {handSide} hand: {handGo != null})", LogCat.VR);
                return;
            }
            var pfxName = pfxNameOverride ?? $"MFX_{mfxName.ToUpper()}_INIT";
            var pfxLeaf = _meshService.CreateVobPfx(pfxName, parent: handGo);
            // The spell's own VISUALFX visual before the generic fallbacks: MFX_ICEWAVE_INIT doesn't exist,
            // spellFX_Icewave uses MFX_IceCUBE_INIT (a fireball was shown in the hand).
            if (pfxLeaf == null && pfxNameOverride == null)
            {
                var ownVisuals = new[]
                {
                    _vmCacheService.TryGetVfxData($"spellFX_{mfxName}")?.VisNameS,
                    _vmCacheService.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_INIT")?.VisNameS,
                    // Chain lightning's hand effect is the one its
                    // VISUALFX creates (MFX_Lightning_Origin) - a fireball was shown.
                    Adapters.Player.VRSpellEffect.GetCreatedVisual(_vmCacheService, mfxName)
                };
                foreach (var visName in ownVisuals)
                {
                    if (string.IsNullOrEmpty(visName))
                        continue;
                    pfxName = visName;
                    pfxLeaf = _meshService.CreateVobPfx(pfxName, parent: handGo);
                    if (pfxLeaf != null)
                        break;
                }
            }
            if (pfxLeaf == null)
            {
                pfxName = "MFX_FIREBALL_INIT";
                pfxLeaf = _meshService.CreateVobPfx(pfxName, parent: handGo);
            }
            if (pfxLeaf == null)
            {
                pfxName = "MFX_FIREBOLT_INIT";
                pfxLeaf = _meshService.CreateVobPfx(pfxName, parent: handGo);
            }

            if (pfxLeaf != null)
            {
                // Build() returns pfxGo (child of RootGo). Store RootGo so DestroyHandVfx
                // cleans up the whole tree, not just the inner leaf.
                var parent = pfxLeaf.transform.parent;
                _handVfxGo = (parent != null && parent.gameObject != handGo) ? parent.gameObject : pfxLeaf;

                // All systems loop: the effect stays in the hand as long as the spell is charged (it vanished before).
                // With Gothic's own motion and emission.
                Adapters.Player.VRSpellEffect.PrepareHeldEffect(_handVfxGo, isGothicMotion: true);
                var parentName = _handVfxGo.transform.parent != null ? _handVfxGo.transform.parent.name : "none";
                Logger.Log($"[VRSpellFx] spell VFX '{pfxName}' on {parentName}", LogCat.VR);
            }
        }

        /// <summary>
        /// VISUALFX effects while charging - emFXInvestOrigin_S on the body (teleport:
        /// MFX_TELEPORT_INVEST + its ring, fire rain: INVESTGLOW) and the first invest key (transformation: ORIGIN in
        /// the hand + GLOW around it). They stay until the spell is cast or cancelled.
        /// </summary>
        public void SpawnInvestFx(bool isHandKeyUsed)
        {
            var mfxName = _mfxName;
            if (mfxName == null)
                return;

            SpawnFxChain(_vmCacheService.TryGetVfxData($"spellFX_{mfxName}")?.EmFxInvestOriginS, true);

            var investKey = _vmCacheService.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_INVEST_1");
            // Throwables swap the hand effect per invest level themselves (NextInvestLevel).
            if (isHandKeyUsed && !string.IsNullOrEmpty(investKey?.VisNameS))
                SpawnHandVfx(HandSide, investKey.VisNameS);
            SpawnFxChain(investKey?.EmCreateFxId, true);

            _bodyInvestLevel = 1;
            CreateInvestLight(mfxName, investKey);
        }

        /// <summary>
        /// The spell's light while charging, at the body (OpenGothic: the spell
        /// effect's light with the INVEST key) - the key's lightPresetName or the spell's, the key's lightRange
        /// (summoning: 0 while held, 4 m from the first level). Gone with the other charging effects.
        /// </summary>
        private void CreateInvestLight(string mfxName, ParticleEffectEmitKeyInstance investKey)
        {
            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null)
                return;
            var preset = !string.IsNullOrEmpty(investKey?.LightPresetName)
                ? investKey.LightPresetName
                : _vmCacheService.TryGetVfxData($"spellFX_{mfxName}")?.LightPresetName;
            var range = investKey != null && investKey.LightRange > 0f ? investKey.LightRange / 100f : -1f;
            _investLight = _particleService.CreateVisualFxLight(preset, hero.Go.transform,
                hero.Go.transform.position + Vector3.up * _bodyFxHeight, range);
            if (_investLight == null)
                return;
            Logger.Log($"[VRSpellFx] Spell light {preset} while charging ({range:F1} m)", LogCat.VR);
        }

        /// <summary>
        /// SPL_NEXTLEVEL of a spell charged at the body (summoning, fire rain): like the engine's next INVEST key -
        /// its lightRange, its emCreateFXID (summoning's third level: FX_EarthQuake). Throwables: NextInvestLevel.
        /// </summary>
        public void NextBodyInvestLevel()
        {
            if (_bodyInvestLevel >= MaxInvestLevel)
                return;
            _bodyInvestLevel++;
            var mfxName = _mfxName;
            var levelKey = _vmCacheService.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_INVEST_{_bodyInvestLevel}");
            if (levelKey == null)
                return;
            if (levelKey.LightRange > 0f)
                _investLight?.SetRange(levelKey.LightRange / 100f);
            SpawnFxChain(levelKey.EmCreateFxId, true);
            Logger.Log($"[VRSpellFx] {mfxName} charge level {_bodyInvestLevel}", LogCat.VR);
        }

        /// <summary>
        /// The CAST key's emCreateFXID: fire rain around the caster, teleport flash, transformation cloud.
        /// </summary>
        public void SpawnCastFx(NpcContainer target)
        {
            var mfxName = _mfxName;
            if (mfxName == null)
                return;
            var castKey = _vmCacheService.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_CAST");
            SpawnFxChain(castKey?.EmCreateFxId, false, target);
            SpawnCastLight(mfxName, castKey);
        }

        /// <summary>
        /// The spell's light flashes when it's cast (OpenGothic: the spell effect
        /// with the CAST key and its light) - summoning's red REDAMBIENCE around the caster. Thrown spells carry
        /// their own light on the projectile.
        /// </summary>
        private void SpawnCastLight(string mfxName, ParticleEffectEmitKeyInstance castKey)
        {
            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null)
                return;
            var preset = !string.IsNullOrEmpty(castKey?.LightPresetName)
                ? castKey.LightPresetName
                : _vmCacheService.TryGetVfxData($"spellFX_{mfxName}")?.LightPresetName;
            var range = castKey != null && castKey.LightRange > 0f ? castKey.LightRange / 100f : -1f;
            var light = _particleService.CreateVisualFxLight(preset, null,
                hero.Go.transform.position + Vector3.up * _bodyFxHeight, range);
            light?.FadeOut(_castLightFadeSeconds, _castLightSeconds);
        }

        /// <summary>
        /// One VISUALFX instance (+ its emFXCreate_S chain, ParticleService): at the hand for ZS_*HAND origins,
        /// otherwise at the body (BIP01, about hip height). Charging effects stay (looping, following), cast effects end
        /// on their own. The spell's own cast sound already plays (PlayCastSound).
        /// </summary>
        private void SpawnFxChain(string fxName, bool isWhileInvesting, NpcContainer castTarget = null)
        {
            var fx = string.IsNullOrEmpty(fxName) ? null : _vmCacheService.TryGetVfxData(fxName);
            if (fx == null)
                return;

            var hero = _npcService.GetHeroContainer();
            var isHandOrigin = fx.EmTrjOriginNode != null &&
                               fx.EmTrjOriginNode.Contains("HAND", System.StringComparison.OrdinalIgnoreCase);
            // The spell comes from the throwing hand (throwables), otherwise from the rune hand.
            var handGo = _vrPlayerService.GetHandModelGo(HandSide);
            // An aimed spell's cast effect appears at its victim (fear: the face above the target, not the caster).
            var target = !isWhileInvesting && castTarget?.Go != null ? castTarget.Go.transform : null;
            // Area spells spread from the caster, wherever the VR hand is (ice wave, fire rain).
            var mfxName = _mfxName;
            var isAreaSpell = !isWhileInvesting && mfxName != null && SpellConst.AoeEffectNames.Contains(mfxName);
            Transform origin;
            if (target != null)
                origin = target;
            else if (isHandOrigin && handGo != null && !isAreaSpell)
                origin = handGo.transform;
            else if (hero?.Go != null)
                origin = hero.Go.transform;
            else
            {
                Logger.LogWarning($"[VRSpellFx] Spell FX {fxName} has no origin (no hand, target or hero)",
                    LogCat.VR);
                return;
            }

            var position = origin == handGo?.transform || origin == target
                ? origin.position
                : origin.position + Vector3.up * GetBodyFxHeight(fx.VisNameS);
            // shpType MESH effects glow on the VR body (teleport silhouette).
            if (_heroBody == null)
                _heroBody = Object.FindFirstObjectByType<VRHeroBody>();
            var bodyMesh = _heroBody != null ? _heroBody.BodyRenderer : null;
            var created = _particleService.PlayVisualFx(fxName, position, isWhileInvesting ? origin : null,
                isWhileInvesting, $"MFX_{mfxName}_Cast", bodyMesh);
            if (isWhileInvesting)
                _bodyFxGos.AddRange(created);
            var at = target != null ? "target" : isHandOrigin ? "hand" : "body";
            Logger.Log($"[VRSpellFx] Spell FX {fxName} at {at} ({created.Count} PFX)", LogCat.VR);
        }

        /// <summary>
        /// Gothic places body effects at BIP01 (about hip height) - one shifted
        /// down by its PFX (summoning's fire ring: shpOffsetVec_s "0 -120 0") is lifted so it lies on the ground.
        /// </summary>
        private float GetBodyFxHeight(string pfxName)
        {
            if (string.IsNullOrEmpty(pfxName))
                return _bodyFxHeight;
            var offset = _vmCacheService.TryGetPfxData(pfxName)?.ShpOffsetVecS?
                .Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (offset == null || offset.Length < 2 ||
                !float.TryParse(offset[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var offsetY) || offsetY >= 0f)
                return _bodyFxHeight;
            return Mathf.Max(_bodyFxHeight, -offsetY / 100f + _groundFxClearance);
        }

        /// <summary>
        /// emCreateFXID of an invest key (spellFX_Fireball_InVEST_BLAST1..4): a one-shot burst in the hand.
        /// </summary>
        public void SpawnInvestBurst(string fxId)
        {
            if (string.IsNullOrEmpty(fxId))
                return;

            var fx = _vmCacheService.TryGetVfxData(fxId);
            var handGo = _vrPlayerService.GetHandModelGo(HandSide);
            if (fx == null || handGo == null)
            {
                Logger.LogWarning($"[VRSpellFx] Invest burst {fxId} skipped (VISUALFX found: {fx != null}, " +
                                  $"hand found: {handGo != null})", LogCat.VR);
                return;
            }

            if (!string.IsNullOrEmpty(fx.VisNameS))
            {
                var pfxGo = _meshService.CreateVobPfx(fx.VisNameS, handGo.transform.position, handGo.transform.rotation,
                    destroyAfterPlay: true);
                if (pfxGo != null)
                    Object.Destroy(pfxGo.transform.parent != null ? pfxGo.transform.parent.gameObject : pfxGo,
                        _collideFxSeconds);
            }
            if (!string.IsNullOrEmpty(fx.SfxId))
                PlayOneShot(fx.SfxId);
        }

        /// <summary>
        /// Collision effect of the spell (emFXCollDyn_S on NPCs, emFXCollStat_S on the world) - per invest level if it
        /// has keys (spellFX_Fireball_COLLIDE_KEY_INVEST_1..4).
        /// </summary>
        public void SpawnCollideFx(string fxName, int level, Vector3 position, Vector3 normal)
        {
            if (string.IsNullOrEmpty(fxName))
                return;

            var levelKey = _vmCacheService.TryGetVfxEmitKey($"{fxName}_KEY_INVEST_{Mathf.Clamp(level, 1, 4)}");
            var fx = _vmCacheService.TryGetVfxData(fxName);
            var pfxName = !string.IsNullOrEmpty(levelKey?.VisNameS) ? levelKey.VisNameS : fx?.VisNameS;
            var sfxName = !string.IsNullOrEmpty(levelKey?.SfxId) ? levelKey.SfxId : fx?.SfxId;

            if (!string.IsNullOrEmpty(pfxName))
            {
                var rotation = normal.sqrMagnitude > 0f ? Quaternion.LookRotation(normal) : Quaternion.identity;
                var pfxGo = _meshService.CreateVobPfx(pfxName, position, rotation, destroyAfterPlay: true);
                if (pfxGo != null)
                {
                    var root = pfxGo.transform.parent != null ? pfxGo.transform.parent.gameObject : pfxGo;
                    Object.Destroy(root, _collideFxSeconds);
                }
            }

            if (!string.IsNullOrEmpty(sfxName))
            {
                var clip = _audioService.GetRandomSoundClip(sfxName);
                if (clip != null)
                    AudioSource.PlayClipAtPoint(clip, position);
            }

            // emFXCreate_S of the collide effect (e.g. fire storm: SPREAD -> burning area).
            if (!string.IsNullOrEmpty(fx?.EmFxCreateS))
                _particleService.PlayVisualFx(fx.EmFxCreateS, position);
        }

        public void DestroyBodyFx()
        {
            _investLight?.FadeOut(_bodyFxFadeSeconds);
            _investLight = null;
            foreach (var fxGo in _bodyFxGos)
            {
                if (fxGo == null)
                    continue;
                foreach (var fxLight in fxGo.GetComponentsInChildren<VisualFxLight>(true))
                    fxLight.FadeOut(_bodyFxFadeSeconds);
                foreach (var ps in fxGo.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Object.Destroy(fxGo, _bodyFxFadeSeconds);
            }
            _bodyFxGos.Clear();
        }

        public void DestroyHandVfx()
        {
            if (_handVfxGo == null) return;
            Object.Destroy(_handVfxGo);
            _handVfxGo = null;
        }

        public void StartInvestSound()
        {
            var mfxName = _mfxName;
            if (mfxName == null) return;
            var clip = _audioService.GetRandomSoundClip($"MFX_{mfxName}_Invest");
            if (clip == null) return;
            _investAudioSource.clip = clip;
            _investAudioSource.Play();
        }

        public void StopInvestSound()
        {
            if (_investAudioSource != null && _investAudioSource.isPlaying)
                _investAudioSource.Stop();
        }

        public void PlayCastSound()
        {
            var mfxName = _mfxName;
            if (mfxName == null) return;
            var sfxName = $"MFX_{mfxName}_Cast";
            var clip = _audioService.GetRandomSoundClip(sfxName);
            // Fallback for spells with no dedicated cast SFX.
            if (clip == null)
                clip = _audioService.GetRandomSoundClip("MFX_Thunderbolt_Cast");
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, _owner.transform.position);
        }

        public void PlayOneShot(string sfxName)
        {
            var clip = _audioService.GetRandomSoundClip(sfxName);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, _owner.transform.position);
        }

        /// <summary>
        /// Light has no Daedalus-side effect to invoke — it's a hardcoded engine toggle in the
        /// original game too. State lives on the hero NpcContainer (not this component) so the light
        /// survives ungrabbing the rune; casting Light again is what turns it back off.
        /// </summary>
        public void ToggleLightSpell()
        {
            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null)
            {
                Logger.LogWarning("[VRSpellFx] Light spell without a hero object - no light", LogCat.VR);
                return;
            }

            if (hero.ActiveLightGo != null)
            {
                Object.Destroy(hero.ActiveLightGo);
                hero.ActiveLightGo = null;
                _stationaryLightsService.ClearDynamicLight();
                Logger.Log("[VRSpellFx] Light spell OFF", LogCat.VR);
                return;
            }

            var lightGo = new GameObject("SpellLight");
            lightGo.transform.SetParent(hero.Go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.6f, 0f); // roughly head height

            // StationaryLight (not a plain Light) so the world's baked-lighting shader — which never
            // samples ordinary Unity/URP realtime lights, only its own static per-renderer index array
            // — also picks this up via the one slot reserved for a runtime-moving light. The attached
            // Light component (added by [RequireComponent]) still covers items/hands/NPCs normally.
            var stationaryLight = lightGo.AddComponent<StationaryLight>();
            stationaryLight.Inject();
            stationaryLight.Type = LightType.Point;
            stationaryLight.Color = _lightColor;
            stationaryLight.Range = _lightRange;
            stationaryLight.Intensity = _lightIntensity;
            stationaryLight.Index = _stationaryLightsService.DynamicLightIndex;
            stationaryLight.Init();

            _meshService.CreateVobPfx("MFX_LIGHT_INIT", parent: lightGo);

            hero.ActiveLightGo = lightGo;
            Logger.Log("[VRSpellFx] Light spell ON", LogCat.VR);
            _unityMonoService.StartCoroutine(AutoTurnOffLight(hero, lightGo));
            _unityMonoService.StartCoroutine(DriveDynamicLight(lightGo, stationaryLight));
        }

        /// <summary>
        /// Periodically pushes the light's current (moving-with-the-player) world position into the
        /// global shader arrays and re-gathers nearby renderers, so the world-lighting effect actually
        /// follows the player instead of staying fixed at the cast position. Not done every frame —
        /// each update re-uploads the whole global light array — a few times a second is plenty for a
        /// walking-speed light.
        /// </summary>
        private IEnumerator DriveDynamicLight(GameObject lightGo, StationaryLight stationaryLight)
        {
            var linearColor = _lightColor.linear;

            while (lightGo != null)
            {
                _stationaryLightsService.UpdateDynamicLight(lightGo.transform.position, _lightRange, linearColor);
                stationaryLight.Refresh();
                // Immersion setting "Light refresh" (Low/Medium/High).
                yield return new WaitForSeconds(_stationaryLightsService.MovingLightRefreshSeconds);
            }
        }

        /// <summary>
        /// Compares against the current ActiveLightGo (not a cancelled-coroutine flag) so it's a no-op
        /// if the player already toggled the light off — or back on again — before this fires.
        /// </summary>
        private IEnumerator AutoTurnOffLight(NpcContainer hero, GameObject lightGo)
        {
            yield return new WaitForSeconds(_lightDurationSeconds);
            if (hero.ActiveLightGo != lightGo) yield break;

            Object.Destroy(lightGo);
            hero.ActiveLightGo = null;
            _stationaryLightsService.ClearDynamicLight();
            Logger.Log("[VRSpellFx] Light spell expired", LogCat.VR);
        }
    }
}
