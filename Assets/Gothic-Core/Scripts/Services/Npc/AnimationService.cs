using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Models.Animations;
using GUZ.Core.Models.Container;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services.Caches;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Services.Npc
{
    public class AnimationService
    {
        private const float _movementThreshold = 0.3f; // If magnitude of first and last frame positions is higher than this, we have a movement animation.

        [ItemCanBeNull]
        private Dictionary<string, AnimationTrack> _tracks = new();

        [Inject] private readonly ResourceCacheService _resourceCacheService;


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

            var name = GetCombinedAnimationKey(mdsName, animName);

            if (_tracks.TryGetValue(name, out var track))
            {
                return track;
            }

            var mds = _resourceCacheService.TryGetModelScript(mdsName)!;

            var anim = mds.Animations.FirstOrDefault(i => i.Name.EqualsIgnoreCase(animName));
            IAnimationAlias animAlias = null;

            // AniAlias lookup
            // Looking up multiple animation types (Animation/AniAlias) for the actual animation name.
            // HINT: This also means, that we potentially create a Track based on duplicate animation data for AniAlias.
            //       As we have no memory issue, this is neglectable for now.
            if (anim == null)
            {
                // FIXME - Alias values aren't overwritten as of today, they need to be handled:
                //         aniAlias (ANI_NAME LAYER NEXT_ANI BLEND_IN BLEND_OUT FLAGS ALIAS_NAME ANI_DIR)
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
                _tracks.Add(name, null);
                return null;
            }
            animName = anim.Name; // We need to use the actual name, as we might have an alias.

            var modelAnimation = _resourceCacheService.TryGetModelAnimation(mdsName, animName);
            if (modelAnimation == null)
            {
                // Caching an empty track means, we don't need to try creating this track again.
                _tracks.Add(name, null);
                return null;
            }

            // For animations: mdhName == mdsName (with different file ending of course ;-))
            var mdhName = mdsName;
            var mdh = _resourceCacheService.TryGetModelHierarchy(mdhName);

            track = CreateTrack(modelAnimation, mdh, anim, animAlias);
            AddTrackDuration(track, modelAnimation);
            SetClipMovementSpeed(track, modelAnimation, mdh);

            _tracks.Add(name, track);

            return track;
        }

        private AnimationTrack CreateTrack(IModelAnimation modelAnimation,
            IModelHierarchy modelHierarchy, IAnimation anim, IAnimationAlias animAlias)
        {
            var track = new AnimationTrack(anim, animAlias, modelAnimation);

            // Get bone names from model hierarchy using node indices
            track.BoneNames = modelAnimation.NodeIndices
                .Select(nodeIndex => modelHierarchy.Nodes[nodeIndex].Name)
                .ToArray();
            
            track.BoneNamesDictionary = new Dictionary<string, int>(modelAnimation.NodeCount);
            // Store the actual node indices used by this animation
            var animationNodeIndices = modelAnimation.NodeIndices.ToArray(); 
            for (int i = 0; i < animationNodeIndices.Length; i++)
            {
                var actualNodeIndex = animationNodeIndices[i];
                var boneName = modelHierarchy.Nodes[actualNodeIndex].Name;
                // Map bone name to its index within this animation track's bone list (0 to BoneCount-1)
                track.BoneNamesDictionary.Add(boneName, i); 
            }

            // Process animation samples
            track.BoneCount = modelAnimation.NodeCount;
            track.FrameCount = modelAnimation.FrameCount;
            track.KeyFrames = new AnimationKeyFrame[modelAnimation.Samples.Count];
            track.FrameTime = 1 / modelAnimation.Fps;

            for (var frameIndex = 0; frameIndex < modelAnimation.Samples.Count / track.BoneCount; frameIndex++)
            {
                for (var nodeIndex = 0; nodeIndex < track.BoneCount; nodeIndex++)
                {
                    var sampleIndex = frameIndex * track.BoneCount + nodeIndex;
                    var sample = modelAnimation.Samples[sampleIndex];
                    var boneName = track.BoneNames[nodeIndex];

                    track.KeyFrames[sampleIndex] = new AnimationKeyFrame
                    {
                        Position = sample.Position.ToUnityVector(),
                        Rotation = new Quaternion(
                            sample.Rotation.X,
                            sample.Rotation.Y,
                            sample.Rotation.Z,
                            -sample.Rotation.W) // Note: W is negated as per original code
                    };

                    // Special handling for root bone (BIP01)
                    if (boneName == Constants.Animations.RootBoneName)
                    {
                        track.KeyFrames[sampleIndex].Position = Vector3.zero;
                    }
                }
            }

            if (track.Flags.HasFlag(AnimationFlags.Rotate))
            {
                Logger.LogWarning($"{track.Name}: Rotation animations are not supported yet.", LogCat.Animation);
            }

            return track;
        }

        private void AddTrackDuration(AnimationTrack track, IModelAnimation modelAnimation)
        {
            track.Duration = modelAnimation.FrameCount / modelAnimation.Fps;
        }

        /// <summary>
        /// Based on first node (BIP01), we calculate its start position and end position of the animation.
        /// If it's above a threshold, we have a movement animation.
        ///
        /// We don't use Flags.Move as also idle animations would move the characters (based on animation data).
        /// Using our own calculation is a workaround found on OpenGothic.
        /// </summary>
        private void SetClipMovementSpeed(AnimationTrack track, IModelAnimation modelAnim, IModelHierarchy mdh)
        {
            // We assume, that only lowest level animations are movement animations. (e.g. S_WALKL)
            if (track.Layer != 1)
            {
                return;
            }

            var firstBoneIndex = modelAnim.NodeIndices.First();
            var isRootBoneExisting = mdh.Nodes[firstBoneIndex].Name == Constants.Animations.RootBoneName;

            // I don't think it will ever happen, but better safe than sorry.
            if (!isRootBoneExisting)
            {
                return;
            }

            var boneCount = modelAnim.NodeCount;
            var firstSample = modelAnim.Samples[0];
            var lastSample = modelAnim.Samples[modelAnim.SampleCount - boneCount];

            // We track xz axis for movement check only.
            // Otherwise, e.g. T_STAND_2_SIT will be marked as "move"
            var unityFirstSamplePos = firstSample.Position.ToUnityVector();
            var unityLastSamplePos = lastSample.Position.ToUnityVector();
            var firstSampleMovePos = new Vector3(unityFirstSamplePos.x, 0, unityFirstSamplePos.z);
            var lastSampleMovePos = new Vector3(unityLastSamplePos.x, 0, unityLastSamplePos.z);
            var movementCheck = lastSampleMovePos - firstSampleMovePos;

            if (movementCheck.sqrMagnitude < _movementThreshold)
            {
                return;
            }

            track.IsMoving = true;
            
            // For the actual movement, we also include y-axis (for climbing ladders/jumping later; not yet tested though').
            var movement = unityLastSamplePos - unityFirstSamplePos;
            
            // TODO - We can also check if we do a "movement" calculation based on each frame. Then animations might "wiggle" during walk instead of walking on a rubber band.
            track.MovementSpeed = movement / track.Duration;
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

            string weaponStateString;
            if (overrideWeaponState.HasValue)
                weaponStateString = overrideWeaponState.Value.ToString();
            else if (fightMode == VmGothicEnums.WeaponState.NoWeapon)
                weaponStateString = "";
            else
                weaponStateString = fightMode.ToString();
            
            var walkModeString = GetWalkModeString((VmGothicEnums.WalkMode)npc.Vob.AiHuman.WalkMode);
            var animationName = type switch
            {
                VmGothicEnums.AnimationType.Idle => GetIdleAnimationName(weaponStateString, walkModeString),
                VmGothicEnums.AnimationType.Move => $"{GetIdleAnimationName(weaponStateString, walkModeString)}L",
                VmGothicEnums.AnimationType.Attack => $"s_{weaponStateString}Attack",
                VmGothicEnums.AnimationType.MoveL => $"t_{weaponStateString}{walkModeString}StrafeL",
                VmGothicEnums.AnimationType.MoveR => $"t_{weaponStateString}{walkModeString}StrafeR",
                VmGothicEnums.AnimationType.RotL => $"T_{weaponStateString}{walkModeString}TurnL",
                VmGothicEnums.AnimationType.RotR => $"T_{weaponStateString}{walkModeString}TurnR",
                VmGothicEnums.AnimationType.StumbleA => "T_Stumble",
                VmGothicEnums.AnimationType.DeadA => "T_Dead",
                VmGothicEnums.AnimationType.DeadB => "T_DeadB",
                VmGothicEnums.AnimationType.StumbleB or
                VmGothicEnums.AnimationType.NoAnim or
                VmGothicEnums.AnimationType.MoveBack or
                VmGothicEnums.AnimationType.WhirlL or
                VmGothicEnums.AnimationType.WhirlR or
                VmGothicEnums.AnimationType.Fall or
                VmGothicEnums.AnimationType.FallDeep or
                VmGothicEnums.AnimationType.FallDeepA or
                VmGothicEnums.AnimationType.FallDeepB or
                VmGothicEnums.AnimationType.Jump or
                VmGothicEnums.AnimationType.JumpUpLow or
                VmGothicEnums.AnimationType.JumpUpMid or
                VmGothicEnums.AnimationType.JumpUp or
                VmGothicEnums.AnimationType.JumpHang or
                VmGothicEnums.AnimationType.Fallen or
                VmGothicEnums.AnimationType.FallenA or
                VmGothicEnums.AnimationType.FallenB or
                VmGothicEnums.AnimationType.SlideA or
                VmGothicEnums.AnimationType.SlideB or
                VmGothicEnums.AnimationType.UnconsciousA or
                VmGothicEnums.AnimationType.UnconsciousB or
                VmGothicEnums.AnimationType.InteractIn or
                VmGothicEnums.AnimationType.InteractOut or
                VmGothicEnums.AnimationType.InteractToStand or
                VmGothicEnums.AnimationType.InteractFromStand or
                VmGothicEnums.AnimationType.AttackL or
                VmGothicEnums.AnimationType.AttackR or
                VmGothicEnums.AnimationType.AttackBlock or
                VmGothicEnums.AnimationType.AttackFinish or
                VmGothicEnums.AnimationType.AimBow or
                VmGothicEnums.AnimationType.PointAt or
                VmGothicEnums.AnimationType.ItmGet or
                VmGothicEnums.AnimationType.ItmDrop or
                VmGothicEnums.AnimationType.MagNoMana or
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };

            // Some monsters like Gobbo are *W1H* with their Mace, but the animations are *Fist* only.
            if (GetTrack(animationName, npc.Props.MdsNameBase, npc.Props.MdsNameOverlay) == null &&
                overrideWeaponState == null)
            {
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
                VmGothicEnums.WalkMode.Water => "WATER",
                VmGothicEnums.WalkMode.Swim => "SWIM",
                VmGothicEnums.WalkMode.Dive => "DIVE",
                _ => throw new ArgumentOutOfRangeException(nameof(walkMode), walkMode, null)
            };
        }

        /// <summary>
        /// Will be reused. Therefore, it's a separate method.
        /// </summary>
        private string GetIdleAnimationName(string weaponStateString, string walkModeString)
        {
            return $"S_{weaponStateString}{walkModeString}";
        }
    }
}
