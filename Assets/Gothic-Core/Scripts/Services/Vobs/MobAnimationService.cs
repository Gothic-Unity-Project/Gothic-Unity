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
using ZenKit.Daedalus;
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
        [Inject] private readonly IMobAnimatorFactory _mobAnimatorFactory;

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

        // The mob the hero uses while his mob dialog is open (the smithy), see StartHeroUse.
        private VobContainer _heroMob;
        private NpcContainer _hero;


        public void Init()
        {
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(WarmWorldMobs);
            GlobalEventDispatcher.MobDialogEnded.AddListener(StopHeroUse);
            GlobalEventDispatcher.LoadGameStart.AddListener(ForgetHeroUse);
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(ForgetHeroUse);
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
        /// The mob's state changed (from -> to, -1 = not used). With a user (an NPC at the mob, the hero at his smithy)
        /// the transition plays, then the state's loop while the user stays; -1 ends the user's usage (Stop). Without
        /// a user (the hero pulled a lever) only the transition plays and the mob rests in its last pose.
        /// A second user of a mob already in the state (or higher) just joins it - the mob goes on for the others.
        /// </summary>
        public void OnStateChanged([CanBeNull] VobContainer mob, [CanBeNull] NpcContainer user, int from, int to)
        {
            if (!IsAnimated(mob))
                return;

            if (to < 0)
            {
                Stop(mob, user);
                return;
            }

            var animator = _mobAnimatorFactory.TryGet(mob);
            if (user != null && animator != null && HasOtherUser(animator, user) && to <= animator.State)
            {
                animator.AddUser(user);
                return;
            }

            var visualName = mob.Vob.Visual.Name;
            var skeleton = GetSkeleton(visualName);
            if (skeleton == null)
                return;

            var transition = GetTrack(visualName, GetTransitionName(from, to));
            var loop = user != null ? GetTrack(visualName, GetLoopName(to)) : null;
            if (transition == null && loop == null)
                return;

            animator ??= _mobAnimatorFactory.GetOrCreate(mob, skeleton);
            if (animator == null)
                return;

            animator.AddUser(user);
            animator.Play(to, transition, loop);
            Logger.LogEditor($"[MobAnimation] {mob.Vob.Name} ({visualName}): {GetStateName(from)} -> " +
                             $"{GetStateName(to)} by {user?.Instance?.GetName(NpcNameSlot.Slot0) ?? "nobody"}",
                LogCat.Vob);
        }

        /// <summary>
        /// The user doesn't use the mob any more. The last one stops its animation: a running transition ends, the
        /// transition back plays (T_S1_2_S0, a lever swings back), then the mob rests.
        /// </summary>
        public void Stop([CanBeNull] VobContainer mob, [CanBeNull] NpcContainer user)
        {
            if (mob == null || user == null)
                return;

            var animator = _mobAnimatorFactory.TryGet(mob);
            if (animator == null || !animator.HasUser(user))
                return;

            animator.RemoveUser(user);
            if (animator.UserCount > 0)
                return;

            var state = animator.State;
            var back = state > 0 ? GetTrack(mob.Vob.Visual.Name, GetTransitionName(state, state - 1)) : null;
            animator.PlayBackAndStop(back);
            Logger.LogEditor($"[MobAnimation] {mob.Vob.Name}: last user left - stopping from {GetStateName(state)}.",
                LogCat.Vob);
        }

        /// <summary>
        /// The hero uses a mob with a dialog (the anvil's smithing choices, the grindstone, a rune table): it works
        /// like for a smith, S0 -> S1 and its loop, until StopHeroUse (the dialog closed). One at a time. VR: a water
        /// pipe's mouthpiece at the mouth too (VRWaterPipe) - it bubbles and smokes, nothing else.
        /// </summary>
        public void StartHeroUse([CanBeNull] VobContainer mob, [CanBeNull] NpcContainer hero)
        {
            StopHeroUse();
            if (hero == null || !IsAnimated(mob))
                return;

            _heroMob = mob;
            _hero = hero;
            OnStateChanged(mob, hero, 0, 1);
        }

        /// <summary>
        /// Ends the hero's use only if it is this mob: a water pipe taken from the mouth doesn't stop the anvil whose
        /// dialog opened meanwhile.
        /// </summary>
        public void StopHeroUse([CanBeNull] VobContainer mob)
        {
            if (mob != null && mob == _heroMob)
                StopHeroUse();
        }

        public void StopHeroUse()
        {
            if (_heroMob == null)
                return;

            var mob = _heroMob;
            var hero = _hero;
            _heroMob = null;
            _hero = null;
            Stop(mob, hero);
        }

        /// <summary>
        /// The world is left (world change, a loaded game): its mobs are gone with it, so the hero's mob is just
        /// forgotten - a dialog anchor that went with the world never raised MobDialogEnded.
        /// </summary>
        private void ForgetHeroUse()
        {
            _heroMob = null;
            _hero = null;
        }

        /// <summary>
        /// The user still uses the mob: the hero while his mob dialog is open, an NPC while it's there (active, not
        /// dead or knocked out, the mob is its CurrentInteractable). MobAnimator asks once per loop round.
        /// </summary>
        public bool IsStillUsing([CanBeNull] VobContainer mob, [CanBeNull] NpcContainer user)
        {
            if (mob == null || user?.Props == null || user.IsDown())
                return false;

            if (mob == _heroMob && user == _hero)
                return true;

            return user.Go != null && user.Go.activeInHierarchy && user.PrefabProps?.CurrentInteractable == mob;
        }

        private static bool HasOtherUser(IMobAnimator animator, NpcContainer user)
        {
            return animator.UserCount > (animator.HasUser(user) ? 1 : 0);
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
