using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Domain.Npc;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Burning after fire damage, like the engine: the VISUALFX VOB_BURN flames come out of the victim's body, with a
    /// small fire light, the burn sound and the s_Fire_Victim dance. Ends by itself, a new fire hit restarts the time.
    /// No damage over time: the reference engine (OpenGothic) has none, the scripts don't define it. Found on
    /// BoroBongo's branch.
    /// </summary>
    public class NpcBurnService
    {
        public const int DamageFire = 1 << 3;

        private const string _burnEffectName = "VOB_BURN";
        private const string _victimAnimation = "S_FIRE_VICTIM";
        private const float _burnSeconds = 4f;
        private const float _bodyFlamesPerSecond = 40f;
        private static readonly Color _fireLightColor = new(1f, 0.55f, 0.2f);

        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly AnimationService _animationService;

        private readonly SpellEffectDomain _spellEffects = new SpellEffectDomain().Inject();
        private readonly Dictionary<NpcContainer, float> _burnEndTimes = new();

        public bool IsBurning(NpcContainer npc)
        {
            return _burnEndTimes.ContainsKey(npc);
        }

        /// <summary>
        /// Sets the NPC on fire, or keeps it burning longer.
        /// </summary>
        public void Ignite(NpcContainer npc)
        {
            if (npc?.Go == null || npc.PrefabProps == null || npc.IsDown())
                return;

            var isBurning = IsBurning(npc);
            _burnEndTimes[npc] = Time.time + _burnSeconds;
            PlayVictimAnimation(npc);
            if (isBurning)
                return;

            var effect = _spellEffects.GetEffect(_burnEffectName);
            var objects = new List<GameObject>();
            SpawnBodyFlames(npc, effect?.VisNameS, objects);
            objects.Add(CreateLightAndSound(npc, effect?.SfxId));
            npc.PrefabProps.StartCoroutine(Burn(npc, objects));
            Logger.Log($"[NpcBurn] {npc.Instance.GetName(NpcNameSlot.Slot0)} burns.", LogCat.Fight);
        }

        private IEnumerator Burn(NpcContainer npc, List<GameObject> objects)
        {
            while (_burnEndTimes.TryGetValue(npc, out var endTime) && Time.time < endTime && npc.Go != null)
                yield return null;

            _burnEndTimes.Remove(npc);
            foreach (var go in objects)
            {
                if (go == null)
                    continue;
                foreach (var particles in go.GetComponentsInChildren<ParticleSystem>(true))
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Object.Destroy(go, 1f);
            }
        }

        /// <summary>
        /// The flames come out of the whole skinned body (body, head, armor). Gothic's own boxes per bone (30x5x5 cm,
        /// 4 particles per second) were thin and hung next to the body.
        /// </summary>
        private void SpawnBodyFlames(NpcContainer npc, string pfxName, List<GameObject> objects)
        {
            if (string.IsNullOrEmpty(pfxName))
                return;

            foreach (var bodyMesh in npc.Go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (bodyMesh.sharedMesh == null)
                    continue;

                var flames = _spellEffects.CreateParticles(pfxName, bodyMesh.transform.position,
                    bodyMesh.transform.rotation, 0f);
                if (flames == null)
                    continue;

                flames.transform.SetParent(bodyMesh.transform, true);
                foreach (var particles in flames.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var shape = particles.shape;
                    shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                    shape.skinnedMeshRenderer = bodyMesh;
                    shape.position = Vector3.zero;
                    // The PFX's box (30x5x5 cm) would squeeze the body into a thin column.
                    shape.scale = Vector3.one;
                    shape.rotation = Vector3.zero;
                    var main = particles.main;
                    main.loop = true;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    var emission = particles.emission;
                    emission.rateOverTime = _bodyFlamesPerSecond;
                    particles.Play();
                }
                objects.Add(flames);
            }
        }

        private GameObject CreateLightAndSound(NpcContainer npc, string sfxName)
        {
            var go = new GameObject("Burn light");
            go.transform.SetParent(npc.Go.transform, false);
            go.transform.localPosition = Vector3.up;
            var fireLight = go.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = _fireLightColor;
            fireLight.range = 4f;
            fireLight.intensity = 1.5f;

            var clip = string.IsNullOrEmpty(sfxName) ? null : _audioService.GetRandomSoundClip(sfxName);
            if (clip != null)
            {
                var source = go.AddComponent<AudioSource>();
                source.clip = clip;
                source.loop = true;
                source.spatialBlend = 1f;
                source.Play();
            }
            return go;
        }

        /// <summary>
        /// The "dance", for those whose model has it.
        /// </summary>
        private void PlayVictimAnimation(NpcContainer npc)
        {
            if (npc.IsHero() || npc.Vob.GetAttribute((int)NpcAttribute.HitPoints) <= 0 ||
                _animationService.GetTrack(_victimAnimation, npc.Props.MdsNameBase, npc.Props.MdsNameOverlay) == null)
                return;

            npc.Props.AnimationQueue.Clear();
            npc.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(_victimAnimation), npc));
        }
    }
}
