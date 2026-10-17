using System;
using System.Collections.Generic;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
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
        [Inject] private readonly Gothic.Core.Services.Config.ConfigService _npcSpellConfigService;

        private const int _maxFxChainDepth = 3;
        private const string _defaultBloodEmitter = "PFX_BLOOD";
        // Gothic's 4 cm drops are hard to see in VR.
        private const float _bloodSizeScale = 2.5f;
        private const float _oneShotFxSeconds = 5f;
        private const float _npcSpellDefaultSpeed = 15f;
        private const float _npcSpellMaxSeconds = 4f;
        private const float _npcSpellHandHeight = 1.3f;
        private const float _npcSpellHandForward = 0.5f;

        public void Init()
        {
            GlobalEventDispatcher.FightHit.AddListener(EmitBlood);
        }

        /// <summary>
        /// Plays a VISUALFX instance (spellFX_*, VOB_*): its PFX upright at the position (following `follow` if set),
        /// its sound and its emFXCreate_S chain. One-shot effects end on their own; looping ones (isLooping) stay
        /// until the caller destroys the returned roots. Camera effects (FX_EarthQuake, ...) are skipped.
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
            if (string.IsNullOrEmpty(fxName) || depth > _maxFxChainDepth ||
                fxName.StartsWith("FX_", StringComparison.OrdinalIgnoreCase))
                return;
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
        /// DeveloperConfig.EnableNpcSpellFx: what an NPC's spell looks like. A spell flying to its target (CAST key
        /// emTrjMode_S TARGET: fire ball, ice block, Mroczne Tajemnice's crawler web) flies there as its PFX and hits
        /// on arrival (onHit). Other spells (sleep, summoning) show their cast effect at the caster and hit at once.
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

            var flight = flightGo.AddComponent<NpcSpellFlight>();
            flight.Target = target.Go.transform;
            flight.Speed = castKey.EmTrjEaseVel / 100f > 0f ? castKey.EmTrjEaseVel / 100f : _npcSpellDefaultSpeed;
            flight.MaxSeconds = _npcSpellMaxSeconds;
            // DeveloperConfig.NpcSpellHoming: a step aside dodges it.
            flight.Homing = _npcSpellConfigService.Dev.NpcSpellHoming;
            flight.OnArrive = () =>
            {
                onHit?.Invoke();
                // Area spells play it on everyone they reach (FightService).
                if (!SpellConst.AoeEffectNames.Contains(mfxName))
                    PlaySpellHitFx(mfxName, target);
            };
            Logger.Log($"[NpcSpellFx] {caster.Instance.GetName(ZenKit.Daedalus.NpcNameSlot.Slot0)} casts {mfxName} " +
                       $"({pfxName}, {flight.Speed:F1} m/s) at {target.Instance.GetName(ZenKit.Daedalus.NpcNameSlot.Slot0)}",
                LogCat.Fight);
            return true;
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
