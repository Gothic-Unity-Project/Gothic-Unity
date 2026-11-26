using System.Collections.Generic;
using Gothic.Core.Domain.Audio;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Animations;
using Gothic.Core.Services.Audio;
using Gothic.Core.Services.Meshes;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Adapters.Animations
{
    /// <summary>
    /// Plays the sound (*eventSFX) and particle (*eventPFX, *eventPFXStop) events of an animation track instance
    /// whose frames were crossed this frame (AnimationTrackInstance.GetPending*). Owned by the object that plays the
    /// animation: AnimationSystem (NPCs) and MobAnimator (used mobs).
    ///
    /// Two ways to handle particles:
    /// - one-off (NPCs): every event builds its effect, which destroys itself after playing (_pfxMaxSeconds at most).
    /// - reusing (mobs): an effect is built on its first event and replayed on the next ones - an anvil hit twice per
    ///   loop rebuilt its sparks (meshes, materials) every time otherwise. DestroyAllPfx() removes them.
    /// </summary>
    public class AnimationEffectPlayer
    {
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly GroundSoundService _groundSoundService;

        // *eventPFX effects that run until their *eventPFXStop (same index). Safety: gone after this time anyway.
        private const float _pfxMaxSeconds = 12f;
        private const float _pfxFadeSeconds = 8f;

        /// <summary>
        /// An effect of the reusing mode, built once: the event it belongs to (name + bone) and its particle systems.
        /// </summary>
        private struct ReusedPfx
        {
            public string Name;
            public string Bone;
            public int Index;
            public GameObject Root;
            public ParticleSystem[] Systems;
        }

        private readonly Transform _boneRoot;
        private readonly Transform _stepOrigin;
        private readonly AudioSource _sound;
        private readonly bool _isReusingPfx;

        // One-off mode: effects with an index, until their *eventPFXStop.
        private readonly Dictionary<int, GameObject> _activePfx = new();
        private readonly List<int> _activePfxIndices = new();

        // Reusing mode: every effect built so far.
        private readonly List<ReusedPfx> _reusedPfx = new();


        /// <param name="boneRoot">The bones of *eventPFX are searched below it; effects without a bone start at it.
        /// </param>
        /// <param name="stepOrigin">Where a step sound (*eventSFXGrnd) asks for the ground below.</param>
        /// <param name="sound">Plays the *eventSFX sounds (one at a time, like the engine's model sound).</param>
        /// <param name="isReusingPfx">Build each effect once and replay it (mobs), see class summary.</param>
        public AnimationEffectPlayer(Transform boneRoot, Transform stepOrigin, AudioSource sound, bool isReusingPfx)
        {
            _boneRoot = boneRoot;
            _stepOrigin = stepOrigin;
            _sound = sound;
            _isReusingPfx = isReusingPfx;
        }

        /// <summary>
        /// Plays the sounds whose frame was crossed. Returns true if there were any (also skipped footsteps).
        /// </summary>
        public bool ApplySfxEvents(AnimationTrackInstance trackInstance)
        {
            var sfxEvents = trackInstance.GetPendingSoundEffects();
            if (sfxEvents == null)
                return false;

            foreach (var sfx in sfxEvents)
            {
                // A step - its sound by the ground below (RUN_EARTH, RUN_STONE), on top of the other sounds (it mustn't
                // cut a swing or a scream). Skipped if the player turned footsteps off (Gothic.ini footstepSounds).
                if (sfx.Name.StartsWith(GroundSoundDomain.GroundEventPrefix))
                {
                    if (!_groundSoundService.IsEnabled)
                        continue;
                    var stepName = _groundSoundService.GetSoundName(
                        sfx.Name.Substring(GroundSoundDomain.GroundEventPrefix.Length), _stepOrigin.position);
                    var stepClip = _audioService.GetRandomSoundClip(stepName);
                    if (stepClip != null)
                        _sound.PlayOneShot(stepClip);
                    continue;
                }

                var clip = _audioService.GetRandomSoundClip(sfx.Name);
                _sound.clip = clip;
                _sound.maxDistance = _audioService.GetSfxRangeMeters(sfx.Name, sfx.Range.ToMeter());
                _sound.Play();
            }

            return sfxEvents.Count > 0;
        }

        /// <summary>
        /// *eventPFX (frame, index, name, bone, ATTACH) creates the effect at the
        /// bone (following it if attached), *eventPFXStop (frame, index) lets it fade out. Joint smoke, bubbles, ...
        /// </summary>
        public void ApplyPfxEvents(AnimationTrackInstance trackInstance)
        {
            var stops = trackInstance.GetPendingParticleEffectStops();
            if (stops != null)
            {
                foreach (var stop in stops)
                    StopPfx(stop.Index);
            }

            var pfxEvents = trackInstance.GetPendingParticleEffects();
            if (pfxEvents == null)
                return;

            foreach (var pfx in pfxEvents)
            {
                if (_isReusingPfx && TryReplayPfx(pfx))
                    continue;

                var root = CreatePfx(pfx, trackInstance.AnimationName);
                if (root == null)
                    continue;

                if (_isReusingPfx)
                {
                    _reusedPfx.Add(new ReusedPfx
                    {
                        Name = pfx.Name,
                        Bone = pfx.Position,
                        Index = pfx.Index,
                        Root = root,
                        Systems = root.GetComponentsInChildren<ParticleSystem>()
                    });
                    continue;
                }

                if (pfx.Index != 0)
                {
                    StopPfx(pfx.Index);
                    _activePfx[pfx.Index] = root;
                    _activePfxIndices.Add(pfx.Index);
                }
                Object.Destroy(root, _pfxMaxSeconds);
            }
        }

        /// <summary>
        /// Builds the effect of a *eventPFX. Returns its root (the effect and its sub emitters), null if it failed.
        /// </summary>
        private GameObject CreatePfx(IEventParticleEffect pfx, string animationName)
        {
            var bone = FindBone(pfx.Position) ?? _boneRoot;
            // A reused effect lives as long as the animator: it must not destroy itself after its first play.
            var isDestroyedAfterPlay = !_isReusingPfx;
            var pfxGo = pfx.Attached
                ? _meshService.CreateVobPfx(pfx.Name, parent: bone.gameObject, destroyAfterPlay: isDestroyedAfterPlay)
                : _meshService.CreateVobPfx(pfx.Name, bone.position, bone.rotation,
                    destroyAfterPlay: isDestroyedAfterPlay);
            if (pfxGo == null)
            {
                Logger.LogWarning($"[AnimationPfx] {pfx.Name} couldn't be created ({animationName}).",
                    LogCat.Animation);
                return null;
            }

            var parent = pfxGo.transform.parent;
            var root = parent != null && parent != bone ? parent.gameObject : pfxGo;
            foreach (var particleSystem in root.GetComponentsInChildren<ParticleSystem>())
            {
                var main = particleSystem.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
            }

            // A mob's effect stays with the mob (culled with it, destroyed with the world).
            if (_isReusingPfx && !pfx.Attached)
                root.transform.SetParent(_boneRoot, true);

            return root;
        }

        /// <summary>
        /// Reusing mode: the effect of this event was built before - it starts again. Stopping its emission first
        /// restarts a burst that is still running (the second anvil hit), its particles in the air stay.
        /// </summary>
        private bool TryReplayPfx(IEventParticleEffect pfx)
        {
            for (var i = 0; i < _reusedPfx.Count; i++)
            {
                var reused = _reusedPfx[i];
                if (reused.Root == null || !reused.Name.EqualsIgnoreCase(pfx.Name) ||
                    !reused.Bone.EqualsIgnoreCase(pfx.Position))
                    continue;

                foreach (var particleSystem in reused.Systems)
                {
                    if (particleSystem == null)
                        continue;
                    particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    particleSystem.Play(false);
                }
                return true;
            }

            return false;
        }

        private void StopPfx(int index)
        {
            if (_isReusingPfx)
            {
                // Reusing mode: the effect only stops emitting, the next event of its index starts it again.
                for (var i = 0; i < _reusedPfx.Count; i++)
                {
                    if (_reusedPfx[i].Index == index)
                        StopEmitting(_reusedPfx[i].Systems);
                }
                return;
            }

            if (!_activePfx.Remove(index, out var pfxGo))
                return;
            _activePfxIndices.Remove(index);
            if (pfxGo == null)
                return;

            foreach (var particleSystem in pfxGo.GetComponentsInChildren<ParticleSystem>())
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(pfxGo, _pfxFadeSeconds);
        }

        /// <summary>
        /// Every effect running until its *eventPFXStop fades out now (one-off mode). In reusing mode all effects stop
        /// emitting and are destroyed once their particles faded.
        /// </summary>
        public void StopAllPfx()
        {
            if (_isReusingPfx)
            {
                foreach (var reused in _reusedPfx)
                {
                    StopEmitting(reused.Systems);
                    if (reused.Root != null)
                        Object.Destroy(reused.Root, _pfxFadeSeconds);
                }
                _reusedPfx.Clear();
                return;
            }

            // Backwards: StopPfx removes the index from the list.
            for (var i = _activePfxIndices.Count - 1; i >= 0; i--)
                StopPfx(_activePfxIndices[i]);
        }

        private static void StopEmitting(ParticleSystem[] systems)
        {
            foreach (var particleSystem in systems)
            {
                if (particleSystem != null)
                    particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>
        /// MDS bone names differ in case from the bone GameObjects ("Bip01 Head" vs "BIP01 HEAD").
        /// </summary>
        private Transform FindBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
                return null;
            foreach (var child in _boneRoot.GetComponentsInChildren<Transform>())
            {
                if (child.name.EqualsIgnoreCase(boneName))
                    return child;
            }
            return null;
        }
    }
}
