using System;
using System.Collections.Generic;
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

        private const int _maxFxChainDepth = 3;
        private const string _defaultBloodEmitter = "PFX_BLOOD";
        // Gothic's 4 cm drops are hard to see in VR.
        private const float _bloodSizeScale = 2.5f;
        private const float _oneShotFxSeconds = 5f;

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
