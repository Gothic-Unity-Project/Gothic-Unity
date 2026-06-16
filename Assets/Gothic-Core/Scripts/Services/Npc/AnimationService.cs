using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Animations;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using JetBrains.Annotations;
using Reflex.Attributes;
using Unity.Collections;
using UnityEngine;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Resolves Gothic animations (MDS base + overlay) into AnimationTracks. Pose data is baked once into
    /// persistent NativeArrays, sampled and blended at runtime by AnimationPoseJob inside each NPC's
    /// PlayableGraph. (Baking runtime AnimationClips is not possible: AnimationClip.SetCurve only works on
    /// legacy clips outside the Editor, and legacy clips can't be used with Playables.)
    /// </summary>
    public class AnimationService
    {
        // Tracks whose root moves faster than this are movement animations (root motion is applied to the NPC).
        // Speed-based, not displacement-based: short walk cycles of small creatures (e.g. Bloodfly's
        // s_FistWalkL moves only 0.45m per loop) must count, while slow pose drifts (t_Warn) must not.
        private const float _minMovementSpeed = 0.1f; // m/s

        // Gothic bench/throne sitting poses are authored with a flipped Y rotation that must be inverted when
        // applied. Resolved into AnimationTrack.InvertYAxis once at bake time so the per-frame pose path (and
        // every PlayAnimation/StopAnimation) needn't string-match. Compared by the (non-alias) animation name.
        private static readonly HashSet<string> _animationsToInvertYAxis =
            new(StringComparer.OrdinalIgnoreCase) { "S_BENCH_S1", "S_THRONE_S1" };

        // Track cache, two levels: MDS name (as passed by callers, any casing/extension) -> animation name -> track.
        // Both levels ignore case, so the per-PlayAnimation hot path runs without any string normalization
        // allocations (ToLower/extension stripping) - important on mobile chipsets (GC pressure).
        // Inner values can be null: a null track is the negative cache for unknown animation names.
        private readonly Dictionary<string, Dictionary<string, AnimationTrack>> _tracksByMdsVariant = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, AnimationTrack>> _tracksByPreparedMds = new();

        // First track baked per real animation (mds-animName). AniAlias tracks share these native buffers
        // instead of baking duplicates - in HUMANS.MDS every 4th entry is an alias.
        private readonly Dictionary<string, AnimationTrack> _bakedSourceTracks = new();

        // Skeleton data (bone paths, rest pose, root node) per model hierarchy.
        private Dictionary<string, AnimationSkeleton> _skeletons = new();

        [Inject] private readonly ResourceCacheService _resourceCacheService;


        public AnimationService()
        {
            // Tracks and skeletons hold persistent NativeArrays - free them when the game (or PlayMode) ends.
            Application.quitting += DisposeNativeData;
        }

        private void DisposeNativeData()
        {
            Application.quitting -= DisposeNativeData;

            foreach (var mdsTracks in _tracksByPreparedMds.Values)
            {
                foreach (var track in mdsTracks.Values)
                {
                    // Alias tracks share the buffers of their baked source track (OwnsBakedData == false).
                    track?.Dispose();
                }
            }
            _tracksByPreparedMds.Clear();
            _tracksByMdsVariant.Clear();
            _bakedSourceTracks.Clear();

            foreach (var skeleton in _skeletons.Values)
            {
                skeleton.Dispose();
            }
            _skeletons.Clear();
        }


        /// <summary>
        /// Try to load animation based on both MDS values: overlay + base.
        /// </summary>
        public AnimationTrack GetTrack(string animName, string mdsBase, string mdsOverlay)
        {
            var track = GetTrack(animName, mdsOverlay);
            if (track == null)
            {
                track = GetTrack(animName, mdsBase);
            }
            return track;
        }

        private AnimationTrack GetTrack(string animName, string mdsName)
        {
            if (mdsName == null)
            {
                return null;
            }

            var mdsTracks = GetMdsTrackCache(mdsName);

            if (mdsTracks.TryGetValue(animName, out var track))
            {
                return track;
            }

            var mds = _resourceCacheService.TryGetModelScript(mdsName)!;

            var anim = mds.Animations.FirstOrDefault(i => i.Name.EqualsIgnoreCase(animName));
            IAnimationAlias animAlias = null;

            // AniAlias lookup
            // Looking up multiple animation types (Animation/AniAlias) for the actual animation name.
            if (anim == null)
            {
                animAlias = mds.AnimationAliases.FirstOrDefault(i => i.Name.EqualsIgnoreCase(animName));
                if (animAlias != null)
                {
                    anim = mds.Animations.FirstOrDefault(i => i.Name.EqualsIgnoreCase(animAlias.Alias));
                }
            }

            // Nothing found
            if (anim == null)
            {
                // Caching an empty track means, we don't need to try creating this track again.
                mdsTracks.Add(animName, null);
                return null;
            }

            var modelAnimation = _resourceCacheService.TryGetModelAnimation(mdsName, anim.Name);
            if (modelAnimation == null)
            {
                // Caching an empty track means, we don't need to try creating this track again.
                mdsTracks.Add(animName, null);
                return null;
            }

            // For animations: mdhName == mdsName (with different file ending of course ;-))
            var mdhName = mdsName;
            var mdh = _resourceCacheService.TryGetModelHierarchy(mdhName);

            var skeleton = GetSkeleton(mdhName, mdh);

            // Aliases bake bit-identical buffers (samples are always baked forward; reversed Direction is
            // applied at playback via AnimationTrackInstance.CurrentFrame), so all tracks of the same real
            // animation share the buffers of whichever track got baked first.
            var bakedKey = GetCombinedAnimationKey(mdsName, anim.Name);
            _bakedSourceTracks.TryGetValue(bakedKey, out var bakedSource);

            track = CreateTrack(modelAnimation, skeleton, anim, animAlias, bakedSource);
            SetClipMovementSpeed(track, modelAnimation, skeleton);

            if (bakedSource == null)
            {
                _bakedSourceTracks.Add(bakedKey, track);
            }

            mdsTracks.Add(animName, track);

            return track;
        }

        /// <summary>
        /// Per-MDS track dictionary. Callers pass MDS names in different spellings (casing, file extension):
        /// each spelling is normalized exactly once, afterwards lookups are two allocation-free TryGetValues.
        /// </summary>
        private Dictionary<string, AnimationTrack> GetMdsTrackCache(string mdsName)
        {
            if (_tracksByMdsVariant.TryGetValue(mdsName, out var mdsTracks))
            {
                return mdsTracks;
            }

            var preparedKey = GetPreparedKey(mdsName);
            if (!_tracksByPreparedMds.TryGetValue(preparedKey, out mdsTracks))
            {
                mdsTracks = new Dictionary<string, AnimationTrack>(StringComparer.OrdinalIgnoreCase);
                _tracksByPreparedMds.Add(preparedKey, mdsTracks);
            }

            _tracksByMdsVariant.Add(mdsName, mdsTracks);
            return mdsTracks;
        }

        private AnimationTrack CreateTrack(IModelAnimation modelAnimation,
            AnimationSkeleton skeleton, IAnimation anim, IAnimationAlias animAlias, AnimationTrack bakedSource)
        {
            var isAlias = animAlias != null;
            var track = new AnimationTrack
            {
                TrackType = isAlias ? AnimationTrack.Type.Alias : AnimationTrack.Type.Animation,
                Name = anim.Name,
                AliasName = isAlias ? animAlias.Name : null,

                // Alias entries overwrite layer/next/blend/flags/direction of the aliased animation.
                Layer = isAlias ? animAlias.Layer : anim.Layer,
                NextAni = isAlias ? animAlias.Next : anim.Next,
                BlendIn = isAlias ? animAlias.BlendIn : anim.BlendIn,
                BlendOut = isAlias ? animAlias.BlendOut : anim.BlendOut,
                Flags = isAlias ? animAlias.Flags : anim.Flags,
                Direction = isAlias ? animAlias.Direction : anim.Direction,

                FirstFrame = anim.FirstFrame,
                FrameCount = modelAnimation.FrameCount,
                Fps = modelAnimation.Fps,
                FpsSource = modelAnimation.FpsSource,
                Duration = modelAnimation.FrameCount / modelAnimation.Fps,
                FrameTime = 1 / modelAnimation.Fps,

                // Cache() turns the native interop objects into plain managed copies once. The event lists are
                // read-only at runtime, so tracks sharing baked data also share them instead of re-marshalling.
                EventTags = bakedSource?.EventTags ?? anim.EventTags.Select(i => i.Cache()).ToList(),
                SoundEffects = bakedSource?.SoundEffects ?? anim.SoundEffects.Select(i => i.Cache()).ToList(),
                ParticleEffects = bakedSource?.ParticleEffects ?? anim.ParticleEffects.Select(i => i.Cache()).ToList(),
                MorphAnimations = bakedSource?.MorphAnimations ?? anim.MorphAnimations.Select(i => i.Cache()).ToList()
            };

            // Looping if this == next. If an alias is used, we expect the same alias being selected.
            track.IsLooping = track.DisplayName.EqualsIgnoreCase(track.NextAni);

            // Cached so the per-frame event scan can skip the vast majority of tracks without any events.
            track.HasEvents = track.EventTags.Count > 0 || track.SoundEffects.Count > 0 ||
                              track.ParticleEffects.Count > 0 || track.MorphAnimations.Count > 0;

            track.InvertYAxis = _animationsToInvertYAxis.Contains(track.Name);

            if (bakedSource != null)
            {
                track.ShareBakedSamples(bakedSource);
            }
            else
            {
                BakeSamples(track, modelAnimation, skeleton, modelAnimation.NodeIndices.ToArray());
            }

            if (track.Flags.HasFlag(AnimationFlags.Rotate))
            {
                Logger.LogWarning($"{track.Name}: Rotation animations are not supported yet.", LogCat.Animation);
            }

            return track;
        }

        /// <summary>
        /// Bake the .man samples into NativeArrays read by AnimationPoseJob (localPosition + localRotation per
        /// bone per frame). The job interpolates linearly between frames, matching the original game's sampling.
        /// </summary>
        private void BakeSamples(AnimationTrack track, IModelAnimation modelAnimation,
            AnimationSkeleton skeleton, int[] nodeIndices)
        {
            var samples = modelAnimation.Samples;
            var boneCount = nodeIndices.Length;
            var frameCount = Math.Min(track.FrameCount, samples.Count / boneCount);

            track.BoneCount = boneCount;
            track.BakedFrameCount = frameCount;
            track.Positions = new NativeArray<Vector3>(frameCount * boneCount, Allocator.Persistent);
            track.Rotations = new NativeArray<Quaternion>(frameCount * boneCount, Allocator.Persistent);
            track.BoneToNode = new NativeArray<int>(nodeIndices, Allocator.Persistent);

            for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                // The hierarchy root isn't always named BIP01 (e.g. Bloodfly uses "BIP01 CENTER").
                // Its horizontal translation is applied separately as root motion
                // (AnimationSystem.ApplyFinalMovement), so X/Z stay pinned at zero. The vertical channel is
                // kept as an offset from the skeleton rest height: the NPC root rests at RootHeight above the
                // ground (walk capsule), so this offset poses the bone at the animated world height
                // (e.g. Bloodfly flying at 1.25m instead of its 0.4m rest height, jump arcs, sitting down).
                var isRootBone = nodeIndices[boneIndex] == skeleton.RootNodeIndex;

                var previousRotation = Quaternion.identity;
                for (var frame = 0; frame < frameCount; frame++)
                {
                    var sampleIndex = frame * boneCount + boneIndex;
                    var sample = samples[sampleIndex];

                    // W is negated to convert from Gothic's to Unity's coordinate system handedness.
                    var rotation = new Quaternion(sample.Rotation.X, sample.Rotation.Y, sample.Rotation.Z, -sample.Rotation.W);

                    // Keep neighboring quaternions on the same hemisphere so the job Slerps the short way.
                    if (frame > 0 && Quaternion.Dot(previousRotation, rotation) < 0f)
                    {
                        rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                    }
                    previousRotation = rotation;

                    var position = sample.Position.ToUnityVector();
                    track.Positions[sampleIndex] = isRootBone
                        ? new Vector3(0f, position.y - skeleton.RootHeight, 0f)
                        : position;
                    track.Rotations[sampleIndex] = rotation;
                }
            }
        }

        private AnimationSkeleton GetSkeleton(string mdhName, IModelHierarchy mdh)
        {
            var key = GetPreparedKey(mdhName);
            if (_skeletons.TryGetValue(key, out var cachedSkeleton))
            {
                return cachedSkeleton;
            }

            var nodes = mdh.Nodes;
            var skeleton = new AnimationSkeleton
            {
                Paths = new string[nodes.Count],
                RestPositions = new NativeArray<Vector3>(nodes.Count, Allocator.Persistent),
                RestRotations = new NativeArray<Quaternion>(nodes.Count, Allocator.Persistent),
                EmptyBoneMap = new NativeArray<int>(0, Allocator.Persistent),
                RootNodeIndex = -1,
                RootHeight = mdh.RootTranslation.ToUnityVector().y
            };

            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var isRootNode = node.ParentIndex == -1;

                skeleton.Paths[i] = isRootNode
                    ? node.Name
                    : $"{skeleton.Paths[node.ParentIndex]}/{node.Name}";

                if (isRootNode && skeleton.RootNodeIndex == -1)
                {
                    skeleton.RootNodeIndex = i;
                }

                // Same decomposition the mesh builders use to place the bone GameObjects (AbstractMeshBuilder).
                // The root node rests at zero: its position is owned by root motion and pinned in every track.
                var restMatrix = node.Transform.ToUnityMatrix();
                skeleton.RestPositions[i] = isRootNode ? Vector3.zero : restMatrix.GetPosition() / 100f;
                skeleton.RestRotations[i] = restMatrix.rotation;
            }

            _skeletons.Add(key, skeleton);
            return skeleton;
        }

        /// <summary>
        /// Skeleton of the given model script - used by AnimationSystem to build its stream handles and job data.
        /// </summary>
        [CanBeNull]
        public AnimationSkeleton GetSkeleton(string mdsName)
        {
            if (mdsName == null)
            {
                return null;
            }

            var mdh = _resourceCacheService.TryGetModelHierarchy(mdsName, false);
            return mdh == null ? null : GetSkeleton(mdsName, mdh);
        }

        /// <summary>
        /// Resting height of the skeleton root above the feet (mdh RootTranslation.y) in meters.
        /// Needed to place the physics walk capsule: as every clip pins the root bone to local zero, the
        /// capsule below it has to end exactly this far down for the feet to touch the ground.
        /// </summary>
        public float GetRootBoneHeight(string mdsName)
        {
            return GetSkeleton(mdsName)?.RootHeight ?? 0f;
        }

        /// <summary>
        /// Based on the root node, we calculate its start position and end position of the animation.
        /// If XZ movement is above a threshold, it is a movement animation (walk, run, strafe).
        /// Vertical root motion is not part of MovementSpeed: it is baked into the root bone's Y channel
        /// as a pose offset (see BakeSamples), which is sample-exact and never fights the physics capsule.
        ///
        /// We don't use Flags.Move as also idle animations would move the characters (based on animation data).
        /// Using our own calculation is a workaround found on OpenGothic.
        /// </summary>
        private void SetClipMovementSpeed(AnimationTrack track, IModelAnimation modelAnim, AnimationSkeleton skeleton)
        {
            // We assume, that only lowest level animations are movement animations. (e.g. S_WALKL)
            if (track.Layer != 1)
            {
                return;
            }

            // The root isn't always named BIP01 (e.g. Bloodfly's "BIP01 CENTER") - match by hierarchy instead.
            if (modelAnim.NodeIndices.First() != skeleton.RootNodeIndex)
            {
                return;
            }

            var boneCount = modelAnim.NodeCount;
            var firstSample = modelAnim.Samples[0];
            var lastSample = modelAnim.Samples[modelAnim.SampleCount - boneCount];

            var unityFirstSamplePos = firstSample.Position.ToUnityVector();
            var unityLastSamplePos = lastSample.Position.ToUnityVector();

            var movement = unityLastSamplePos - unityFirstSamplePos;

            // Only XZ motion counts: vertical pose motion (fly height, sit, jump arcs) lives in the
            // baked root bone Y channel, not in root motion.
            var xzMovement = new Vector3(movement.x, 0, movement.z);
            if (xzMovement.magnitude / track.Duration >= _minMovementSpeed)
            {
                track.IsMoving = true;
                track.MovementSpeed = xzMovement / track.Duration;

                // Reversed tracks (e.g. t_Bench_S1_2_S0 aliasing t_Bench_S0_2_S1 with direction R) play the
                // samples back to front, so their root motion runs the opposite way too.
                if (track.Direction == AnimationDirection.Backward)
                    track.MovementSpeed = -track.MovementSpeed;
            }
        }

        /// <summary>
        /// .man files are combined of MDSNAME-ANIMATIONNAME.man
        /// </summary>
        private string GetCombinedAnimationKey(string mdsKey, string animKey)
        {
            var preparedMdsKey = GetPreparedKey(mdsKey);
            var preparedAnimKey = GetPreparedKey(animKey);

            return preparedMdsKey + "-" + preparedAnimKey;
        }

        /// <summary>
        /// Basically extract file ending and lower names.
        /// </summary>
        private string GetPreparedKey(string key)
        {
            if (key == null) return string.Empty;
            var lowerKey = key.ToLower();
            var extension = Path.GetExtension(lowerKey);

            if (extension == string.Empty)
            {
                return lowerKey;
            }

            return lowerKey.Replace(extension, "");
        }

        public string GetAnimationName(VmGothicEnums.AnimationType type, NpcContainer npc, VmGothicEnums.WeaponState? overrideWeaponState = null)
        {
            // The name of the currently active weapon == prefix of animation.
            var fightMode = (VmGothicEnums.WeaponState)npc.Vob.FightMode;

            var weaponStateString = GetWeaponAnimationPrefix(overrideWeaponState ?? fightMode);

            var walkMode = (VmGothicEnums.WalkMode)npc.Vob.AiHuman.WalkMode;
            var walkModeString = GetWalkModeString(walkMode);
            var animationName = type switch
            {
                VmGothicEnums.AnimationType.Idle => GetIdleAnimationName(weaponStateString, walkModeString),
                VmGothicEnums.AnimationType.Move => GetMoveAnimationName(weaponStateString, walkMode, walkModeString),
                VmGothicEnums.AnimationType.Attack => $"s_{weaponStateString}Attack",
                VmGothicEnums.AnimationType.MoveL => $"t_{weaponStateString}{walkModeString}StrafeL",
                VmGothicEnums.AnimationType.MoveR => $"t_{weaponStateString}{walkModeString}StrafeR",
                VmGothicEnums.AnimationType.RotL => $"T_{weaponStateString}{walkModeString}TurnL",
                VmGothicEnums.AnimationType.RotR => $"T_{weaponStateString}{walkModeString}TurnR",
                VmGothicEnums.AnimationType.StumbleA => "T_Stumble",
                VmGothicEnums.AnimationType.StumbleB => "T_StumbleB",
                VmGothicEnums.AnimationType.DeadA => "T_Dead",
                VmGothicEnums.AnimationType.DeadB => "T_DeadB",
                // Walking backwards (e.g. s_WalkBL, s_FistWalkBL). A run-backwards loop doesn't exist in the assets.
                VmGothicEnums.AnimationType.MoveBack => $"S_{weaponStateString}{walkModeString}BL",
                VmGothicEnums.AnimationType.AttackL => $"t_{weaponStateString}AttackL",
                VmGothicEnums.AnimationType.AttackR => $"t_{weaponStateString}AttackR",
                // Parades exist per direction (_O/_U/_L/_R); _O (high) is the default block.
                VmGothicEnums.AnimationType.AttackBlock => $"t_{weaponStateString}Parade_O",
                VmGothicEnums.AnimationType.AttackFinish => $"t_{weaponStateString}SFinish",
                VmGothicEnums.AnimationType.AimBow => $"S_{weaponStateString}Aim",
                VmGothicEnums.AnimationType.Fall => "S_FallDn",
                VmGothicEnums.AnimationType.FallDeep or
                VmGothicEnums.AnimationType.FallDeepA => "S_Fall",
                VmGothicEnums.AnimationType.FallDeepB => "S_FallB",
                VmGothicEnums.AnimationType.Fallen or
                VmGothicEnums.AnimationType.FallenA => "S_Fallen",
                VmGothicEnums.AnimationType.FallenB => "S_FallenB",
                VmGothicEnums.AnimationType.Jump => "S_Jump",
                VmGothicEnums.AnimationType.JumpUpLow => "S_JumpUpLow",
                VmGothicEnums.AnimationType.JumpUpMid => "S_JumpUpMid",
                VmGothicEnums.AnimationType.JumpUp => "S_JumpUp",
                VmGothicEnums.AnimationType.JumpHang => "S_Hang",
                VmGothicEnums.AnimationType.SlideA => "S_Slide",
                VmGothicEnums.AnimationType.SlideB => "S_SlideB",
                VmGothicEnums.AnimationType.UnconsciousA => "S_Wounded",
                VmGothicEnums.AnimationType.UnconsciousB => "S_WoundedB",
                VmGothicEnums.AnimationType.PointAt => "T_Point",
                VmGothicEnums.AnimationType.ItmGet => "T_Stand_2_IGet",
                // No animations exist in the assets for these (checked against the original .mds files).
                VmGothicEnums.AnimationType.NoAnim or
                VmGothicEnums.AnimationType.WhirlL or
                VmGothicEnums.AnimationType.WhirlR or
                VmGothicEnums.AnimationType.InteractIn or
                VmGothicEnums.AnimationType.InteractOut or
                VmGothicEnums.AnimationType.InteractToStand or
                VmGothicEnums.AnimationType.InteractFromStand or
                VmGothicEnums.AnimationType.ItmDrop or
                VmGothicEnums.AnimationType.MagNoMana or
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };

            // Wading has no own idle animation in the assets (only the WalkW move/turn/strafe loops exist).
            // Stand with the on-land Walk idle instead.
            if (type == VmGothicEnums.AnimationType.Idle && walkMode == VmGothicEnums.WalkMode.Water &&
                GetTrack(animationName, npc.Props.MdsNameBase, npc.Props.MdsNameOverlay) == null)
            {
                animationName = GetIdleAnimationName(weaponStateString, "WALK");
            }

            // Some monsters like Gobbo are *W1H* with their Mace, but the animations are *Fist* only.
            if (GetTrack(animationName, npc.Props.MdsNameBase, npc.Props.MdsNameOverlay) == null &&
                overrideWeaponState == null)
            {
                Logger.LogWarning(
                    $"[AnimFallback] >{npc.Go.name}< anim >{animationName}< not found " +
                    $"(base={npc.Props.MdsNameBase}, overlay={npc.Props.MdsNameOverlay}, fightMode={(VmGothicEnums.WeaponState)npc.Vob.FightMode}) → falling back to Fist",
                    LogCat.Animation);
                return GetAnimationName(type, npc, VmGothicEnums.WeaponState.Fist);
            }

            return animationName;
        }

        private string GetWalkModeString(VmGothicEnums.WalkMode walkMode)
        {
            return walkMode switch
            {
                VmGothicEnums.WalkMode.Walk => "WALK",
                VmGothicEnums.WalkMode.Run => "RUN",
                VmGothicEnums.WalkMode.Sneak => "SNEAK",
                // Wading is named WalkW inside the .mds files (s_WalkWL, t_WalkWTurnL, ...).
                VmGothicEnums.WalkMode.Water => "WALKW",
                VmGothicEnums.WalkMode.Swim => "SWIM",
                VmGothicEnums.WalkMode.Dive => "DIVE",
                _ => throw new ArgumentOutOfRangeException(nameof(walkMode), walkMode, null)
            };
        }

        /// <summary>
        /// Animation names carry these weapon prefixes (e.g. s_1hRun, t_2hRunTurnL, s_MagWalk).
        /// The WeaponState enum names (W1H, W2H, Mage) do not match the names inside the .mds files.
        /// </summary>
        public static string GetWeaponAnimationPrefix(VmGothicEnums.WeaponState weaponState)
        {
            return weaponState switch
            {
                VmGothicEnums.WeaponState.NoWeapon => "",
                VmGothicEnums.WeaponState.Fist => "FIST",
                VmGothicEnums.WeaponState.W1H => "1H",
                VmGothicEnums.WeaponState.W2H => "2H",
                VmGothicEnums.WeaponState.Bow => "BOW",
                VmGothicEnums.WeaponState.CBow => "CBOW",
                VmGothicEnums.WeaponState.Mage => "MAG",
                _ => throw new ArgumentOutOfRangeException(nameof(weaponState), weaponState, null)
            };
        }

        private static string GetMoveAnimationName(string weaponStateString, VmGothicEnums.WalkMode walkMode, string walkModeString)
        {
            // Swimming and diving have no walk loop - forward movement is a dedicated F animation without weapon variants.
            if (walkMode is VmGothicEnums.WalkMode.Swim or VmGothicEnums.WalkMode.Dive)
                return $"S_{walkModeString}F";

            return $"{GetIdleAnimationName(weaponStateString, walkModeString)}L";
        }

        /// <summary>
        /// Will be reused. Therefore, it's a separate method.
        /// </summary>
        private static string GetIdleAnimationName(string weaponStateString, string walkModeString)
        {
            return $"S_{weaponStateString}{walkModeString}";
        }
    }
}
