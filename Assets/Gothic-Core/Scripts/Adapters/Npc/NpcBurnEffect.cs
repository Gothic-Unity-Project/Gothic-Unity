using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Npc
{
    /// <summary>
    /// Burning after fire damage, like the engine: the VISUALFX VOB_BURN chain (MFX_Firespell_HUMANBURN on BIP01 FIRE,
    /// both upper arms and hands - each link on its own emTrjOriginNode bone), a small fire light, the burn sound and
    /// the "dance" s_Fire_Victim (NpcBurnService). Ends by itself; a new fire hit restarts the time.
    /// Created by NpcBurnEffectFactory.
    /// </summary>
    public class NpcBurnEffect : MonoBehaviour, INpcBurnEffect
    {
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly AudioService _audioService;

        private const string _burnFxName = "VOB_BURN";
        private const float _burnSeconds = 4f;
        private const int _maxChainLength = 10;
        private const float _bodyFlamesPerSecond = 40f;
        private static readonly Color _fireLightColor = new(1f, 0.55f, 0.2f);

        private readonly List<GameObject> _fxGos = new();
        private NpcContainer _npc;
        private float _remainingSeconds;

        /// <summary>
        /// Sets the NPC on fire (or keeps it burning longer).
        /// </summary>
        public void Burn(NpcContainer npc)
        {
            _npc = npc;
            var isAlreadyBurning = _remainingSeconds > 0f;
            _remainingSeconds = _burnSeconds;

            if (isAlreadyBurning)
                return;

            SpawnBurnChain();
            Logger.Log($"[NpcBurnEffect] {npc.Instance.GetName(NpcNameSlot.Slot0)} burns ({_fxGos.Count} fire objects)",
                LogCat.Fight);
        }

        private void SpawnBurnChain()
        {
            var burnFx = _vmCacheService.TryGetVfxData(_burnFxName);
            if (burnFx == null)
            {
                Logger.LogWarning($"[NpcBurnEffect] VISUALFX {_burnFxName} not found - no fire.", LogCat.Fight);
                return;
            }

            AddLightAndSound(burnFx.SfxId);

            // Like the teleport glow on the hero: the flames come out of the whole skinned body (body, head, armor).
            // Gothic's own boxes per bone (30x5x5 cm, 4 particles/s) were thin and hung next to the body.
            if (!string.IsNullOrEmpty(burnFx.VisNameS) && SpawnBodyFlames(burnFx.VisNameS))
                return;

            SpawnBoneChain();
        }

        private bool SpawnBodyFlames(string pfxName)
        {
            var isAnySpawned = false;
            foreach (var bodyMesh in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (bodyMesh.sharedMesh == null)
                    continue;

                var anchor = new GameObject($"Burn_{bodyMesh.name}");
                anchor.transform.SetParent(bodyMesh.transform, false);
                var pfx = _meshService.CreateVobPfx(pfxName, parent: anchor, isFullRate: true);
                if (pfx == null)
                {
                    Destroy(anchor);
                    continue;
                }

                foreach (var particles in anchor.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var shape = particles.shape;
                    shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                    shape.skinnedMeshRenderer = bodyMesh;
                    shape.position = Vector3.zero;
                    // The PFX's BOX size (30x5x5 cm) stays in the shape and squeezed the body into a thin column in
                    // its middle - the flames came out of the NPC's center instead of its surface.
                    shape.scale = Vector3.one;
                    shape.rotation = Vector3.zero;
                    var main = particles.main;
                    main.loop = true;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    var emission = particles.emission;
                    emission.rateOverTime = _bodyFlamesPerSecond;
                    particles.Play();
                }
                _fxGos.Add(anchor);
                isAnySpawned = true;
            }
            return isAnySpawned;
        }

        /// <summary>
        /// Without a skinned mesh: Gothic's own VOB_BURN chain, each link on its emTrjOriginNode bone.
        /// </summary>
        private void SpawnBoneChain()
        {
            var fxName = _burnFxName;
            for (var i = 0; i < _maxChainLength && !string.IsNullOrEmpty(fxName); i++)
            {
                var fx = _vmCacheService.TryGetVfxData(fxName);
                if (fx == null)
                {
                    if (i == 0)
                        Logger.LogWarning($"[NpcBurnEffect] VISUALFX {_burnFxName} not found - no fire.", LogCat.Fight);
                    break;
                }

                if (!string.IsNullOrEmpty(fx.VisNameS))
                {
                    var bone = string.IsNullOrEmpty(fx.EmTrjOriginNode)
                        ? null
                        : transform.FindChildRecursively(fx.EmTrjOriginNode);
                    var anchor = new GameObject($"Burn_{fxName}");
                    anchor.transform.SetParent(bone != null ? bone : transform, false);
                    if (bone == null)
                        anchor.transform.localPosition = Vector3.up;

                    var pfx = _meshService.CreateVobPfx(fx.VisNameS, parent: anchor, isFullRate: true);
                    if (pfx == null)
                    {
                        Destroy(anchor);
                    }
                    else
                    {
                        foreach (var particles in anchor.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            var main = particles.main;
                            main.loop = true;
                            particles.Play();
                        }
                        _fxGos.Add(anchor);
                    }
                }

                fxName = fx.EmFxCreateS;
            }
        }

        private void AddLightAndSound(string sfxId)
        {
            var lightGo = new GameObject("Burn_Light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.up;
            var fireLight = lightGo.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = _fireLightColor;
            fireLight.range = 4f;
            fireLight.intensity = 1.5f;
            _fxGos.Add(lightGo);

            var clip = string.IsNullOrEmpty(sfxId) ? null : _audioService.GetRandomSoundClip(sfxId);
            if (clip == null)
                return;
            var source = lightGo.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 1f;
            source.Play();
        }

        private void Update()
        {
            if (_remainingSeconds <= 0f)
                return;

            _remainingSeconds -= Time.deltaTime;
            if (_remainingSeconds <= 0f)
                Extinguish();
        }

        private void Extinguish()
        {
            foreach (var fxGo in _fxGos)
            {
                if (fxGo == null)
                    continue;
                foreach (var particles in fxGo.GetComponentsInChildren<ParticleSystem>(true))
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(fxGo, 1f);
            }
            _fxGos.Clear();
            _remainingSeconds = 0f;
        }

        // Culled while burning: no Update anymore - put the fire out now.
        private void OnDisable()
        {
            if (_remainingSeconds > 0f)
                Extinguish();
        }
    }
}
