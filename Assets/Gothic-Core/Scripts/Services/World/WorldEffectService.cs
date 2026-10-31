using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Effects;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using Color = UnityEngine.Color;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// Visual effects the scripts start with Wld_PlayEffect and end with Wld_StopEffect, like the engine (OpenGothic
    /// GameScript::wld_playeffect, GlobalEffects): the effect's particles at its origin node (emTrjOriginNode) of the NPC
    /// or item, the particles, sound and created effect of its cast key, its sound and the effects it creates
    /// (emFXCreate_S, after emFXTriggerDelay). G2 uses it a lot - Abuyin's teleport, the stone guardians awaking, the
    /// Dementors' darkness, Beliar's claw - G1 for the Sleeper's fireball.
    /// Four visuals aren't particles but whole-screen effects: screenblend.scx tints the view (ScreenBlendStarted),
    /// earthquake.eqk shakes the world - VR rumbles the controllers instead (EarthquakeStarted) - and time.slw slows the
    /// world down (swampweed). morph.fov, a wobbling field of view, is left out: it makes VR players sick.
    /// A projectile (Wld_PlayEffect's last parameter, the Sleeper's fireball) flies its cast key's particles to the
    /// target at the key's speed and takes the damage off like a spell: per damage type minus the protection.
    /// NPCs with an effect of their own (C_NPC.effect: the fire shadowbeast's flames, the undead dragon's aura) carry it
    /// while they are around, like the engine's MdlVisual::setNpcEffect.
    /// </summary>
    public class WorldEffectService
    {
        private const string _screenBlendVisual = "screenblend.scx";
        private const string _earthquakeVisual = "earthquake.eqk";
        private const string _slowTimeVisual = "time.slw";
        private const string _morphFovVisual = "morph.fov";
        // An effect creating an effect creating an effect... - the scripts' chains are short.
        private const int _maxChainDepth = 4;
        // An effect without a lifespan whose particles don't loop ends after them - at most this long.
        private const float _maxOneShotSeconds = 10f;
        private const float _defaultProjectileSpeed = 15f;
        private const float _maxProjectileSeconds = 8f;
        private const float _projectileHitDistance = 0.6f;
        private const float _impactSeconds = 3f;

        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly NpcBurnService _npcBurnService;

        private readonly SpellEffectDomain _effects = new SpellEffectDomain().Inject();
        private readonly SpellDamageDomain _damage = new SpellDamageDomain().Inject();
        // By the name the scripts started them with: Wld_StopEffect ends the whole chain.
        private readonly Dictionary<string, List<GameObject>> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ScreenBlend>> _screenBlends = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _slowTimes = new(StringComparer.OrdinalIgnoreCase);
        // Started and not stopped - an effect created later (emFXTriggerDelay) only comes while its root runs.
        private readonly HashSet<string> _runningRoots = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<NpcContainer, GameObject> _npcEffects = new();

        public WorldEffectService()
        {
            // A new world or a loaded game starts without effects.
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(StopAll);
            GlobalEventDispatcher.LoadGameStart.AddListener(StopAll);
            GlobalEventDispatcher.NpcMeshCullingChanged.AddListener(OnNpcCullingChanged);
        }

        /// <param name="origin">The NPC or world item the effect starts at.</param>
        /// <param name="target">Who a projectile flies to and who takes the damage.</param>
        /// <param name="damageType">DAM_* bits, the damage is taken off for each.</param>
        public void Play(string effectName, DaedalusInstance origin, DaedalusInstance target, int damage,
            int damageType, bool isProjectile)
        {
            var effect = _effects.GetEffect(effectName);
            if (effect == null)
            {
                Logger.LogWarning($"[WorldEffect] {effectName} is no visual effect - skipped.", LogCat.Vob);
                return;
            }

            var originNpc = (origin as NpcInstance)?.GetUserData();
            var originGo = originNpc?.Go ?? ((origin as ItemInstance)?.UserData as VobContainer)?.Go;
            if (originGo == null)
            {
                Logger.Log($"[WorldEffect] {effectName} has no origin in the world - skipped.", LogCat.Vob);
                return;
            }

            var targetNpc = (target as NpcInstance)?.GetUserData();
            // Without the node (a monster without jaws) an NPC's effect starts at its body like the engine's vob center.
            var anchor = FindNode(originGo.transform, effect.EmTrjOriginNode);
            if (anchor == originGo.transform && originNpc?.PrefabProps?.Bip01 != null)
                anchor = originNpc.PrefabProps.Bip01;
            Logger.Log($"[WorldEffect] {effectName} at {GetName(origin, originGo)}" +
                       (isProjectile ? $", flying to {GetName(target, targetNpc?.Go)}" : "") +
                       (damage > 0 ? $", {damage} damage" : ""), LogCat.Vob);

            if (isProjectile && targetNpc?.Go != null)
            {
                Launch(effectName, effect, anchor.position, originNpc, targetNpc, damage, damageType);
                return;
            }

            _runningRoots.Add(effectName);
            Start(effectName, effectName, effect, anchor, 0);
            if (damage > 0 && targetNpc != null)
                Hit(originNpc, targetNpc, damage, damageType);
        }

        /// <summary>
        /// Ends the effect the scripts started under this name, with everything it created.
        /// </summary>
        public void Stop(string effectName)
        {
            _runningRoots.Remove(effectName);
            if (_objects.Remove(effectName, out var objects))
            {
                foreach (var go in objects)
                    FadeOut(go);
            }

            if (_screenBlends.Remove(effectName, out var blends))
            {
                foreach (var blend in blends)
                    GlobalEventDispatcher.ScreenBlendStopped.Invoke(blend);
            }

            if (_slowTimes.Remove(effectName))
                ApplySlowTime();
        }

        private void OnNpcCullingChanged(NpcContainer npc, Gothic.Core.Adapters.Npc.NpcLoader loader, bool isInVisibleRange,
            bool wasOutOfDistance)
        {
            if (!isInVisibleRange || string.IsNullOrEmpty(npc?.Instance?.Effect))
                return;

            _unityMonoService.StartCoroutine(AttachNpcEffect(npc));
        }

        /// <summary>
        /// The NPC's own effect, looping on its body as long as the body exists.
        /// </summary>
        private IEnumerator AttachNpcEffect(NpcContainer npc)
        {
            // The body is built in the same event.
            yield return null;
            if (npc.Go == null || _npcEffects.TryGetValue(npc, out var existing) && existing != null)
                yield break;

            var effect = _effects.GetEffect(npc.Instance.Effect);
            if (effect == null)
                yield break;

            var anchor = FindNode(npc.Go.transform, effect.EmTrjOriginNode);
            if (anchor == npc.Go.transform && npc.PrefabProps?.Bip01 != null)
                anchor = npc.PrefabProps.Bip01;

            var root = new GameObject($"NpcEffect {npc.Instance.Effect}");
            root.transform.SetParent(anchor, false);
            AddLoopingParticles(effect.VisNameS, root.transform);
            var child = _effects.GetEffect(effect.EmFxCreateS);
            if (child != null)
                AddLoopingParticles(child.VisNameS, root.transform);

            _npcEffects[npc] = root;
            Logger.Log($"[WorldEffect] {npc.Instance.GetName(NpcNameSlot.Slot0)} carries {npc.Instance.Effect}.",
                LogCat.Vob);
        }

        private void AddLoopingParticles(string pfxName, Transform parent)
        {
            var particles = _effects.CreateParticles(pfxName, parent.position, parent.rotation, 0f);
            if (particles == null)
                return;

            particles.transform.SetParent(parent, true);
            foreach (var system in particles.GetComponentsInChildren<ParticleSystem>())
            {
                var main = system.main;
                main.loop = true;
                if (!system.isPlaying)
                    system.Play();
            }
        }

        private void StopAll()
        {
            foreach (var name in new List<string>(_objects.Keys))
                Stop(name);
            foreach (var name in new List<string>(_screenBlends.Keys))
                Stop(name);
            _slowTimes.Clear();
            _runningRoots.Clear();
            _npcEffects.Clear();
            ApplySlowTime();
        }

        private void Start(string rootName, string name, EffectBaseInstance effect, Transform anchor, int depth)
        {
            var visual = effect.VisNameS ?? string.Empty;
            if (visual.EqualsIgnoreCase(_screenBlendVisual))
                StartScreenBlend(rootName, effect);
            else if (visual.EqualsIgnoreCase(_earthquakeVisual))
                StartEarthquake(effect, anchor.position);
            else if (visual.EqualsIgnoreCase(_slowTimeVisual))
                StartSlowTime(rootName, effect);
            else if (!visual.EqualsIgnoreCase(_morphFovVisual))
                AddParticles(rootName, visual, anchor, effect.EmFxLifespan);

            PlaySound(rootName, effect.SfxId, effect.SfxIsAmbient != 0, anchor);

            // Played effects are cast at once: the cast key's particles, sound and effect (the teleport's flash).
            var castKey = _effects.GetKey($"{name}_KEY_CAST");
            if (castKey != null)
            {
                AddParticles(rootName, castKey.VisNameS, anchor, castKey.EmFxLifespan);
                PlaySound(rootName, castKey.SfxId, castKey.SfxIsAmbient != 0, anchor);
                StartChild(rootName, castKey.EmCreateFxId, anchor, 0f, depth);
            }

            StartChild(rootName, effect.EmFxCreateS, anchor, effect.EmFxTriggerDelay, depth);
        }

        private void StartChild(string rootName, string childName, Transform anchor, float delay, int depth)
        {
            if (string.IsNullOrEmpty(childName) || depth >= _maxChainDepth)
                return;

            var child = _effects.GetEffect(childName);
            if (child == null)
                return;

            if (delay > 0f)
                _unityMonoService.StartCoroutine(StartLater(rootName, childName, child, anchor, delay, depth + 1));
            else
                Start(rootName, childName, child, FindNode(anchor, child.EmTrjOriginNode), depth + 1);
        }

        private IEnumerator StartLater(string rootName, string name, EffectBaseInstance effect, Transform anchor,
            float delay, int depth)
        {
            yield return new WaitForSeconds(delay);
            // Stopped or the world changed meanwhile.
            if (anchor != null && _runningRoots.Contains(rootName))
                Start(rootName, name, effect, anchor, depth);
        }

        private void AddParticles(string rootName, string pfxName, Transform anchor, float lifespan)
        {
            // Upright like the engine's FIXED effects, facing where the NPC faces.
            var rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);
            var particles = _effects.CreateParticles(pfxName, anchor.position, rotation, Mathf.Max(lifespan, 0f));
            if (particles == null)
                return;

            particles.transform.SetParent(anchor, true);
            if (lifespan <= 0f && !IsLooping(particles))
                Object.Destroy(particles, GetPlaySeconds(particles));
            Track(rootName, particles);
        }

        private void PlaySound(string rootName, string sfxName, bool isAmbient, Transform anchor)
        {
            var clip = string.IsNullOrEmpty(sfxName) ? null : _audioService.GetRandomSoundClip(sfxName);
            if (clip == null)
                return;

            var go = new GameObject($"Effect sound {sfxName}");
            go.transform.SetParent(anchor, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            // Ambient sounds have no position.
            source.spatialBlend = isAmbient ? 0f : 1f;
            source.Play();
            Object.Destroy(go, clip.length + 0.1f);
            Track(rootName, go);
        }

        private void StartScreenBlend(string rootName, EffectBaseInstance effect)
        {
            var textureName = effect.GetUserString(3);
            var blend = new ScreenBlend
            {
                EffectName = rootName,
                Color = ParseColor(effect.GetUserString(1)),
                FadeSeconds = ParseFloat(effect.GetUserString(2), 0f),
                // Like the engine: the lifespan, else until Wld_StopEffect.
                Seconds = Mathf.Max(effect.EmFxLifespan, 0f),
                TextureName = string.IsNullOrWhiteSpace(textureName) ? null : textureName.Trim(),
                FramesPerSecond = ParseFloat(effect.GetUserString(4), 0f)
            };

            if (!_screenBlends.TryGetValue(rootName, out var blends))
                _screenBlends[rootName] = blends = new List<ScreenBlend>();
            blends.Add(blend);
            GlobalEventDispatcher.ScreenBlendStarted.Invoke(blend);

            if (blend.Seconds > 0f)
                _unityMonoService.StartCoroutine(EndScreenBlend(rootName, blend));
        }

        private IEnumerator EndScreenBlend(string rootName, ScreenBlend blend)
        {
            yield return new WaitForSeconds(blend.Seconds);
            if (_screenBlends.TryGetValue(rootName, out var blends) && blends.Remove(blend))
            {
                if (blends.Count == 0)
                    _screenBlends.Remove(rootName);
                GlobalEventDispatcher.ScreenBlendStopped.Invoke(blend);
            }
        }

        /// <summary>
        /// User strings: radius (cm), seconds, amplitude (cm per axis).
        /// </summary>
        private void StartEarthquake(EffectBaseInstance effect, Vector3 position)
        {
            var radius = ParseFloat(effect.GetUserString(0), 1000f) / 100f;
            var seconds = ParseFloat(effect.GetUserString(1), 5f);
            var amplitude = ParseVector(effect.GetUserString(2)).magnitude / 100f;
            GlobalEventDispatcher.EarthquakeStarted.Invoke(position, radius, seconds, amplitude);
        }

        /// <summary>
        /// User strings: the world's time scale, the player's time scale. The VR player moves by the same clock as the
        /// world, so the world's scale counts. Like the engine the lifespan runs in the slowed time.
        /// </summary>
        private void StartSlowTime(string rootName, EffectBaseInstance effect)
        {
            _slowTimes[rootName] = Mathf.Clamp(ParseFloat(effect.GetUserString(0), 1f), 0.1f, 1f);
            ApplySlowTime();

            if (effect.EmFxLifespan > 0f)
                _unityMonoService.StartCoroutine(EndSlowTime(rootName, effect.EmFxLifespan));
        }

        private IEnumerator EndSlowTime(string rootName, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (_slowTimes.Remove(rootName))
                ApplySlowTime();
        }

        private void ApplySlowTime()
        {
            var scale = 1f;
            foreach (var slowTime in _slowTimes.Values)
                scale = Mathf.Min(scale, slowTime);
            Time.timeScale = scale;
        }

        private void Launch(string name, EffectBaseInstance effect, Vector3 start, NpcContainer caster,
            NpcContainer target, int damage, int damageType)
        {
            // What flies is the cast key's visual (MFX_Fireball_CAST), at its speed (cm/s).
            var castKey = _effects.GetKey($"{name}_KEY_CAST");
            var visualName = !string.IsNullOrEmpty(castKey?.VisNameS) ? castKey.VisNameS : effect.VisNameS;
            var speed = castKey != null && castKey.EmTrjEaseVel > 0f
                ? castKey.EmTrjEaseVel / 100f
                : _defaultProjectileSpeed;

            var visual = _effects.CreateParticles(visualName, start, Quaternion.identity, 0f);
            var anchor = visual != null ? visual.transform : caster?.Go?.transform;
            if (castKey != null && anchor != null)
                PlaySound(name, castKey.SfxId, castKey.SfxIsAmbient != 0, anchor);

            _unityMonoService.StartCoroutine(Fly(effect, visual, start, caster, target, speed, damage, damageType));
        }

        /// <summary>
        /// Follows the target like the engine's TARGET trajectory. A wall in between stops it.
        /// </summary>
        private IEnumerator Fly(EffectBaseInstance effect, GameObject visual, Vector3 position, NpcContainer caster,
            NpcContainer target, float speed, int damage, int damageType)
        {
            var time = 0f;
            while (time < _maxProjectileSeconds && target.Go != null)
            {
                var aim = FightService.GetAimPoint(target);
                var toAim = aim - position;
                var step = speed * Time.deltaTime;
                if (toAim.magnitude <= Mathf.Max(step, _projectileHitDistance))
                {
                    _effects.CreateParticles(_effects.GetEffect(effect.EmFxCollDynS), aim, Quaternion.identity,
                        _impactSeconds);
                    Hit(caster, target, damage, damageType);
                    break;
                }

                var next = position + toAim.normalized * step;
                if (Physics.Linecast(position, next, out var wall, 1 << Constants.DefaultLayer,
                        QueryTriggerInteraction.Ignore))
                {
                    _effects.CreateParticles(_effects.GetEffect(effect.EmFxCollStatS), wall.point, Quaternion.identity,
                        _impactSeconds);
                    break;
                }

                position = next;
                if (visual != null)
                    visual.transform.position = position;
                time += Time.deltaTime;
                yield return null;
            }

            FadeOut(visual);
        }

        private void Hit(NpcContainer caster, NpcContainer target, int damage, int damageType)
        {
            // Without an NPC behind the effect nobody would be to blame.
            if (caster == null || target.Instance == null)
                return;

            var dealt = _damage.CalculateDamage(target, damageType, damage);
            Logger.Log($"[WorldEffect] {target.Instance.GetName(NpcNameSlot.Slot0)} takes {dealt} of {damage} damage.",
                LogCat.Fight);
            _fightService.OnSpellHit(caster, target, dealt, true);
            if (dealt > 0 && (damageType & NpcBurnService.DamageFire) != 0 && !target.IsHero())
                _npcBurnService.Ignite(target);
        }

        private void Track(string rootName, GameObject go)
        {
            if (!_objects.TryGetValue(rootName, out var objects))
                _objects[rootName] = objects = new List<GameObject>();
            objects.RemoveAll(o => o == null);
            objects.Add(go);
        }

        /// <summary>
        /// Particles stop emitting and the flying ones die out, sounds end at once.
        /// </summary>
        private static void FadeOut(GameObject go)
        {
            if (go == null)
                return;

            var particleSystems = go.GetComponentsInChildren<ParticleSystem>();
            foreach (var particles in particleSystems)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(go, particleSystems.Length > 0 ? 2f : 0f);
        }

        private static bool IsLooping(GameObject particles)
        {
            foreach (var system in particles.GetComponentsInChildren<ParticleSystem>())
            {
                if (system.main.loop)
                    return true;
            }
            return false;
        }

        private static float GetPlaySeconds(GameObject particles)
        {
            var seconds = 0f;
            foreach (var system in particles.GetComponentsInChildren<ParticleSystem>())
            {
                var main = system.main;
                seconds = Mathf.Max(seconds, main.duration + main.startLifetime.constantMax);
            }
            return Mathf.Clamp(seconds, 0.5f, _maxOneShotSeconds);
        }

        private static string GetName(DaedalusInstance instance, GameObject go)
        {
            return instance is NpcInstance npc ? npc.GetName(NpcNameSlot.Slot0) : go != null ? go.name : "-";
        }

        /// <summary>
        /// The bone the effect starts at (BIP01, ZS_RIGHTHAND, BIP01 JAWS), else the object itself.
        /// </summary>
        private static Transform FindNode(Transform root, string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
                return root;

            nodeName = nodeName.Trim();
            foreach (var child in root.GetComponentsInChildren<Transform>())
            {
                if (child.name.EqualsIgnoreCase(nodeName))
                    return child;
            }
            return root;
        }

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }

        /// <summary>
        /// "r g b a", 0-255.
        /// </summary>
        private static Color ParseColor(string value)
        {
            var parts = (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var channels = new[] { 0f, 0f, 0f, 255f };
            for (var i = 0; i < parts.Length && i < channels.Length; i++)
                channels[i] = ParseFloat(parts[i], channels[i]);
            return new Color(channels[0] / 255f, channels[1] / 255f, channels[2] / 255f, channels[3] / 255f);
        }

        private static Vector3 ParseVector(string value)
        {
            var parts = (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var axes = new float[3];
            for (var i = 0; i < parts.Length && i < axes.Length; i++)
                axes[i] = ParseFloat(parts[i], 0f);
            return new Vector3(axes[0], axes[1], axes[2]);
        }
    }
}
