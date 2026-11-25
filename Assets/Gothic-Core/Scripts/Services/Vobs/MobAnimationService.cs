using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Animations;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;
using JetBrains.Annotations;
using Reflex.Attributes;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Vobs
{
    /// <summary>
    /// Like the engine's oCMobInter (OpenGothic Interactive::setState), a mob plays its own state animations while an
    /// NPC or the hero changes its state: the anvil rings and sparks, the forge roars, the grindstone turns, a lever
    /// swings. They live in the mob's own model script (BSANVIL_OC.MDS: T_S0_2_S1, S_S1, T_S1_2_S0), not in the
    /// user's. This service looks the tracks up and decides; the MobAnimator adapter on the mob plays them.
    /// Docs/mobs.md.
    /// </summary>
    public class MobAnimationService
    {
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly GameStateService _gameStateService;

        private const string _mdsExtension = ".MDS";
        private const string _standStateName = "STAND";

        // The states a used mob plays in nearly every model script - looked up at world load (see WarmWorldMobs).
        private static readonly (int From, int To)[] _warmedStateChanges = { (0, 1), (1, 0) };
        private static readonly int[] _warmedLoops = { 1 };

        // Model scripts (visual names, any casing) whose tracks are looked up already.
        private readonly HashSet<string> _warmedModels = new(StringComparer.OrdinalIgnoreCase);
        // One-shot logs: a model without a hierarchy, an animation a model doesn't have.
        private readonly HashSet<string> _loggedMissingSkeletons = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loggedMissingTracks = new(StringComparer.OrdinalIgnoreCase);


        public void Init()
        {
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(WarmWorldMobs);
        }

        /// <summary>
        /// The mob plays state animations of its own: an oCMobInter family member with an .MDS visual. Not doors,
        /// chests and beds (the VR hand moves them, IsHingeMob/IsBed) and no fireplaces (FireService burns them).
        /// Mobs with a plain mesh (Fellan's REPAIR_PLANK.MDL) have no animation file.
        /// </summary>
        public bool IsAnimated([CanBeNull] VobContainer mob)
        {
            var vob = mob?.Vob;
            if (vob is not IInteractiveObject || vob is IFire || vob.IsHingeMob() || vob.IsBed())
                return false;

            var visualName = vob.Visual?.Name;
            return !string.IsNullOrEmpty(visualName) &&
                   visualName.EndsWith(_mdsExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The name of a mob state inside its animations: -1 (not used) is STAND, the others S0, S1, ...
        /// </summary>
        private static string GetStateName(int state)
        {
            return state < 0 ? _standStateName : "S" + state;
        }

        private static string GetTransitionName(int from, int to)
        {
            return "T_" + GetStateName(from) + "_2_" + GetStateName(to);
        }

        private static string GetLoopName(int state)
        {
            return "S_" + GetStateName(state);
        }

        /// <summary>
        /// The skeleton of the mob's model (its nodes). Null (logged once) if the model has no hierarchy.
        /// </summary>
        [CanBeNull]
        private AnimationSkeleton GetSkeleton(string visualName)
        {
            var skeleton = _animationService.GetSkeleton(visualName);
            if (skeleton == null && _loggedMissingSkeletons.Add(visualName))
                Logger.LogWarning($"[MobAnimation] {visualName} has no model hierarchy - it won't animate.",
                    LogCat.Vob);
            return skeleton;
        }

        /// <summary>
        /// A track of the mob's own model script. AnimationService strips the extension and remembers missing names.
        /// Most mobs miss some names (T_STAND_2_S0): logged once, in the Editor only.
        /// </summary>
        [CanBeNull]
        private AnimationTrack GetTrack(string visualName, string animationName)
        {
            var track = _animationService.GetTrack(animationName, visualName, null);
            if (track == null && _loggedMissingTracks.Add(visualName + "/" + animationName))
                Logger.LogEditor($"[MobAnimation] {visualName} has no {animationName}.", LogCat.Vob);
            return track;
        }

        /// <summary>
        /// The tracks of the world's mobs are baked while the world loads (P3): the first anvil hit or lever pull
        /// would read and bake the model's .MAN files in the middle of the game otherwise. Their sounds are decoded
        /// too. Each model only once per game session (AnimationService keeps the baked tracks).
        /// </summary>
        private void WarmWorldMobs()
        {
            var watch = Stopwatch.StartNew();
            var modelCount = 0;
            foreach (var mobs in _gameStateService.VobsInteractable.Values)
            {
                foreach (var mob in mobs)
                {
                    if (!IsAnimated(mob))
                        continue;

                    var visualName = mob.Vob.Visual.Name;
                    if (!_warmedModels.Add(visualName))
                        continue;

                    WarmModel(visualName);
                    modelCount++;
                }
            }

            if (modelCount > 0)
                Logger.Log($"[MobAnimation] Animations of {modelCount} mob models baked in " +
                           $"{watch.ElapsedMilliseconds} ms.", LogCat.Vob);
        }

        private void WarmModel(string visualName)
        {
            if (GetSkeleton(visualName) == null)
                return;

            var found = new StringBuilder();
            foreach (var (from, to) in _warmedStateChanges)
                WarmTrack(visualName, GetTransitionName(from, to), found);
            foreach (var state in _warmedLoops)
                WarmTrack(visualName, GetLoopName(state), found);

            var foundNames = found.Length > 0 ? found.ToString() : "no state animations";
            Logger.LogEditor($"[MobAnimation] {visualName}: {foundNames}", LogCat.Vob);
        }

        private void WarmTrack(string visualName, string animationName, StringBuilder found)
        {
            var track = GetTrack(visualName, animationName);
            if (track == null)
                return;

            found.Append(found.Length > 0 ? ", " : string.Empty).Append(animationName);
            if (track.SoundEffects == null)
                return;

            foreach (var sfx in track.SoundEffects)
                _audioService.GetRandomSoundClip(sfx.Name);
        }
    }
}
