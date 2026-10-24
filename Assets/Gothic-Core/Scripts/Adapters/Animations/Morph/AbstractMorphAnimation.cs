using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Animations.Morph;
using Gothic.Core.Services.Caches;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;
using Mesh = UnityEngine.Mesh;
using Random = UnityEngine.Random;

namespace Gothic.Core.Adapters.Animations.Morph
{
    /// <summary>
    ///
    /// </summary>
    public abstract class AbstractMorphAnimation : MonoBehaviour
    {
        [Inject] private readonly MorphMeshCacheService _morphMeshCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        /// <summary>
        /// Multiple morphs can run at the same time. e.g. viseme and eyesblink.
        /// We therefore add animations to a list to calculate their positions together.
        /// </summary>
        private readonly List<MorphAnimationData> _runningMorphs = new();

        // Random face animations retry a missing animation every few seconds. Warn once per name, not every time.
        private readonly HashSet<string> _missingAnimationNames = new();

        private Mesh _mesh;

        /// <summary>
        /// RandomMorphs are blinking eyes in G1.
        /// The settings for it contains two options of blinking firstTime and secondTime.
        /// </summary>
        protected List<(string morphMeshName, string animationName, float firstTimeAverage, float firstTimeVariable,
                float secondTimeAverage, float secondTimeVariable, float probabilityOfFirst, float timer)>
            RandomAnimations = new();

        protected List<float>
            RandomAnimationTimers =
                new(); // It's faster to alter a list entry than rewriting the whole Tuple struct above.

        protected virtual void Start()
        {
            // As we don't set component inside Prefab, we need to assign mesh later at runtime.
            if (_mesh == null && TryGetComponent<MeshFilter>(out var meshFilter))
            {
                _mesh = meshFilter.mesh;
            }
        }

        /// <summary>
        /// Blend all running morphs on top of each other (face: mood + viseme + blink) instead of the last one setting
        /// the whole mesh.
        /// </summary>
        protected virtual bool IsBlendingMorphs => false;

        // IsBlendingMorphs: the summed vertices (reused every frame) and whether the mesh is off its rest pose.
        private Vector3[] _blendedVertices;
        private string _blendedMeshName;
        private bool _isMeshMorphed;
        private bool _isBlendMismatchLogged;

        public bool IsAnimationRunning(string animationName)
        {
            return _runningMorphs.Any(i => !i.IsStopping && i.AnimationName.EqualsIgnoreCase(animationName));
        }

        protected void StartAnimation(string morphMeshName, [CanBeNull] string animationName, float weight = 1f,
            float holdSeconds = 0f)
        {
            // IsBlendingMorphs: the same animation again (a viseme for the next line, the same mood) only refreshes it.
            if (IsBlendingMorphs && animationName != null)
            {
                var running = _runningMorphs.FirstOrDefault(i =>
                    !i.IsStopping && i.AnimationName.EqualsIgnoreCase(animationName));
                if (running != null)
                {
                    running.Weight = Mathf.Clamp01(weight);
                    running.HoldSeconds = holdSeconds;
                    running.HoldTimer = 0f;
                    return;
                }
            }

            var newMorph = new MorphAnimationData();

            newMorph.MeshMetadata = _resourceCacheService.TryGetMorphMesh(morphMeshName);
            if (newMorph.MeshMetadata == null)
            {
                Logger.LogWarning($"MorphMesh not found: {morphMeshName}", LogCat.Mesh);
                return;
            }

            newMorph.AnimationMetadata = animationName == null
                ? newMorph.MeshMetadata.Animations.FirstOrDefault()
                : newMorph.MeshMetadata.Animations.FirstOrDefault(anim => anim.Name.EqualsIgnoreCase(animationName));
            if (newMorph.AnimationMetadata == null)
            {
                if (_missingAnimationNames.Add($"{morphMeshName}/{animationName}"))
                {
                    Logger.LogWarning($"MorphAnimation '{animationName}' not found in '{morphMeshName}'",
                        LogCat.Mesh);
                }
                return;
            }
            newMorph.AnimationFrameData =
                _morphMeshCacheService.TryGetMorphData(morphMeshName, newMorph.AnimationMetadata.Name);
            if (newMorph.AnimationFrameData == null)
                return;

            // IsBlendingMorphs: like the engine, one animation per MMB layer - a new mood fades the old one out (T_EAT and
            // T_HURT share the moods' layer 1, VISEME has layer 2, R_EYESBLINK 4).
            if (IsBlendingMorphs)
            {
                foreach (var sameLayer in _runningMorphs.Where(i =>
                             i.AnimationMetadata.Layer == newMorph.AnimationMetadata.Layer))
                    sameLayer.IsStopping = true;

                newMorph.Weight = Mathf.Clamp01(weight);
                newMorph.HoldSeconds = holdSeconds;
                newMorph.IsLooping = newMorph.AnimationMetadata.Flags.HasFlag(MorphAnimationFlags.Loop);
                _runningMorphs.Add(newMorph);
                return;
            }

            // Reset if already added and playing
            if (_runningMorphs.Any(i => i.MeshName == newMorph.MeshName))
            {
                StopAnimation(newMorph.AnimationMetadata.Name);
            }

            var animationFlags = newMorph.AnimationMetadata.Flags;

            // Not yet implemented warning.
            if (animationFlags.HasFlag(MorphAnimationFlags.Random)
                || animationFlags.HasFlag(MorphAnimationFlags.Shape)
                || animationFlags.HasFlag(MorphAnimationFlags.ShapeReference)
               )
            {
                Logger.LogWarning($"MorphMesh animation with flags {animationFlags} not yet supported!", LogCat.Mesh);
            }

            // TODO/HINT - Is also handled via -1 second duration value of morphAnimation.Duration
            if (animationFlags.HasFlag(MorphAnimationFlags.Loop))
            {
                newMorph.IsLooping = true;
            }

            _runningMorphs.Add(newMorph);
        }

        public void StopAnimation(string animationName)
        {
            // IsBlendingMorphs: fades out with its MMB blend out; not running is fine (a line without a viseme).
            if (IsBlendingMorphs)
            {
                foreach (var morph in _runningMorphs.Where(i => i.AnimationName.EqualsIgnoreCase(animationName)))
                    morph.IsStopping = true;
                return;
            }

            var morphToStop = _runningMorphs.FirstOrDefault(i => i.AnimationName.EqualsIgnoreCase(animationName));

            if (morphToStop == null)
            {
                Logger.LogWarning($"MorphAnimation {animationName} not found on {gameObject.name}", LogCat.Mesh);
                return;
            }

            // Reset to a stable value.
            // Can be missing if this exact morph mesh name's very first registration (CreateMorphMeshBegin/
            // CreateMorphMeshEnd in AbstractMeshBuilder) got interrupted partway - AddVertexMapping marks the
            // name as "already cached" before SetUnityVerticesForVertexMapping ever runs, so every later NPC
            // sharing that head permanently finds no baseline vertices to reset to (seen on a New Balance head
            // mesh, hammering this every frame at ~70/sec while nearby - a real perf hit, not just log spam).
            var originalVertices = _morphMeshCacheService.GetOriginalUnityVertices(morphToStop.MeshName);
            if (originalVertices == null)
            {
                Logger.LogWarning(
                    $"No original vertices cached for morph mesh '{morphToStop.MeshName}' - skipping reset.",
                    LogCat.Mesh);
            }
            else
            {
                _mesh.vertices = originalVertices;
            }

            _runningMorphs.Remove(morphToStop);
        }

        private void Update()
        {
            if (IsBlendingMorphs)
                UpdateBlendedMorphs();
            else
                UpdateRunningMorphs();
            CheckIfRandomAnimationShouldBePlayed();
        }

        /// <summary>
        /// IsBlendingMorphs: like the engine, every running morph adds its offset from the rest pose, weighted by its
        /// intensity and its blend in/out (MMB values are weight per millisecond: a blink closes in 40 ms, a mood in 1 s).
        /// Before, each morph set the whole mesh - a blink or a mood would wipe out the talking mouth.
        /// </summary>
        private void UpdateBlendedMorphs()
        {
            if (_runningMorphs.Count == 0)
            {
                if (_isMeshMorphed)
                {
                    _isMeshMorphed = false;
                    ResetToRestPose(_morphMeshCacheService.GetOriginalUnityVertices(_blendedMeshName));
                }
                return;
            }

            _blendedMeshName = _runningMorphs[0].MeshName;
            var baseVertices = _morphMeshCacheService.GetOriginalUnityVertices(_blendedMeshName);
            if (_mesh == null || baseVertices == null || baseVertices.Length != _mesh.vertexCount)
            {
                if (!_isBlendMismatchLogged)
                {
                    _isBlendMismatchLogged = true;
                    Logger.LogWarning($"[FaceAni] {gameObject.name}: no rest pose for '{_runningMorphs[0].MeshName}' " +
                                      "matching the mesh - face animations off for it", LogCat.Mesh);
                }
                _runningMorphs.Clear();
                return;
            }

            if (_blendedVertices == null || _blendedVertices.Length != baseVertices.Length)
                _blendedVertices = new Vector3[baseVertices.Length];
            Array.Copy(baseVertices, _blendedVertices, baseVertices.Length);

            var deltaMs = Time.deltaTime * 1000f;
            foreach (var morph in _runningMorphs.ToList())
            {
                morph.Time += Time.deltaTime;
                morph.HoldTimer += Time.deltaTime;
                var animation = morph.AnimationMetadata;

                if (morph.IsStopping)
                {
                    var blendOut = Mathf.Abs(animation.BlendOut);
                    morph.BlendWeight = blendOut > 0f ? morph.BlendWeight - deltaMs * blendOut : 0f;
                    if (morph.BlendWeight <= 0f)
                    {
                        _runningMorphs.Remove(morph);
                        continue;
                    }
                }
                else
                {
                    morph.BlendWeight = animation.BlendIn > 0f
                        ? Mathf.Min(1f, morph.BlendWeight + deltaMs * animation.BlendIn)
                        : 1f;
                    if (morph.BlendWeight >= 1f && IsMorphOver(morph))
                        morph.IsStopping = true;
                }

                var frames = morph.AnimationFrameData;
                var frameFloat = morph.Time * 1000f * animation.Speed % frames.Count;
                var frame = Mathf.Clamp((int)frameFloat, 0, frames.Count - 1);
                var current = frames[frame];
                var next = frames[(frame + 1) % frames.Count];
                if (current.Length != baseVertices.Length || next.Length != baseVertices.Length)
                    continue;

                var lerp = frameFloat - frame;
                var weight = morph.BlendWeight * morph.Weight;
                for (var i = 0; i < baseVertices.Length; i++)
                    _blendedVertices[i] += (Vector3.LerpUnclamped(current[i], next[i], lerp) - baseVertices[i]) * weight;
            }

            _mesh.vertices = _blendedVertices;
            _isMeshMorphed = true;
        }

        private void ResetToRestPose(Vector3[] restVertices)
        {
            if (restVertices != null && _mesh != null && restVertices.Length == _mesh.vertexCount)
                _mesh.vertices = restVertices;
        }

        /// <summary>
        /// HoldSeconds > 0: held that long; &lt; 0: until stopped; 0: the MMB duration (R_EYESBLINK 0 = blend in and out,
        /// T_EAT 2 s, a looping mood -1 = until stopped).
        /// </summary>
        private static bool IsMorphOver(MorphAnimationData morph)
        {
            if (morph.HoldSeconds > 0f)
                return morph.HoldTimer >= morph.HoldSeconds;
            if (morph.HoldSeconds < 0f)
                return false;
            var duration = (float)morph.AnimationMetadata.Duration.TotalSeconds;
            return duration >= 0f && morph.HoldTimer >= duration;
        }

        // FIXME - We currently calculate morphs every frame but could lower it's value with 60, 30, 15 frames in mind (e.g. for each distance culling).
        // FIXME - Means less CPU cycles to calculate morphs.
        private void UpdateRunningMorphs()
        {
            // ToList() -> As we might call .Remove() during looping, we need to clone the list to allow it (otherwise we get a CompilerIssue).
            foreach (var morph in _runningMorphs.ToList())
            {
                morph.Time += Time.deltaTime;

                CalculateMorphWeights();

                // IMorphAnimation.Speed is in milliseconds. We therefore multiply current time by 1000.
                var newFrameFloat = morph.Time * 1000 * morph.AnimationMetadata.Speed;
                var newFrameInt = (int)newFrameFloat;

                // We can't use animationtime as (e.g.) for R_EYESBLINK we have only one frame which is a time of 0.0f,
                // but instead we need to say frame 0 is 0...0.999 of first frame.
                if (newFrameInt >= morph.AnimationFrameCount)
                {
                    // We just assume we're exactly at point 0.0f when we reached the end. Not 100% perfect but we're in milliseconds area of error.
                    if (morph.IsLooping)
                    {
                        morph.Time = 0;
                        newFrameFloat = 0.0f;
                        newFrameInt = 0;
                    }
                    else
                    {
                        StopAnimation(morph.AnimationName);
                        continue;
                    }
                }

                var currentMorph = morph.AnimationFrameData[newFrameInt];

                // e.g. R_EYESBLINK
                if (morph.AnimationFrameCount == 1)
                {
                    // FIXME - We need blendin/blendout otherwise this will be a on-off only.
                    var calculatedMorph = new Vector3[currentMorph.Length];
                    for (var i = 0; i < currentMorph.Length; i++)
                    {
                        calculatedMorph[i] = currentMorph[i];
                    }

                    _mesh.vertices = calculatedMorph;
                }
                else
                {
                    var nextMorph =
                        morph.AnimationFrameData[
                            newFrameInt == morph.AnimationMetadata.FrameCount - 1 ? 0 : newFrameInt + 1];

                    var interpolatedMorph = new Vector3[currentMorph.Length];
                    for (var i = 0; i < currentMorph.Length; i++)
                    {
                        interpolatedMorph[i] =
                            Vector3.Lerp(currentMorph[i], nextMorph[i], newFrameFloat - MathF.Truncate(newFrameFloat));
                    }

                    _mesh.vertices = interpolatedMorph;
                }
            }
        }

        private void CalculateMorphWeights()
        {
            // FIXME - Not yet implemented. But will provide smoother animations for e.g. viseme. Keep in mind there are blendIn and blendOut parameters.
        }

        private void CheckIfRandomAnimationShouldBePlayed()
        {
            for (var i = 0; i < RandomAnimations.Count; i++)
            {
                RandomAnimationTimers[i] -= Time.deltaTime;

                if (RandomAnimationTimers[i] > 0)
                {
                    continue;
                }

                var anim = RandomAnimations[i];

                // FIXME - Set maxWeight for animation based on random value.
                // FIXME - var weight = Random.value * 0.4f + 0.6f
                StartAnimation(anim.morphMeshName, anim.animationName);

                if (Random.value < anim.probabilityOfFirst)
                {
                    RandomAnimationTimers[i] = (Random.value * 2 - 1) * anim.firstTimeVariable + anim.firstTimeAverage;
                }
                else
                {
                    RandomAnimationTimers[i] =
                        (Random.value * 2 - 1) * anim.secondTimeVariable + anim.secondTimeAverage;
                }
            }
        }
    }
}
