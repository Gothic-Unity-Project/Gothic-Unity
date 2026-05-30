using System;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Adapters.Animations.Morph;
using GUZ.Core.Adapters.Npc;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Manager;
using GUZ.Core.Models.Animations;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services.Npc;
using GUZ.Core.Services.Vobs;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using AnimationState = GUZ.Core.Models.Animations.AnimationState;
using EventType = ZenKit.EventType;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Adapters.Adnimations
{
    /// <summary>
    /// NPC component to handle animations. The Blending is using the official Gothic animation information:
    /// https://www.worldofgothic.de/modifikation/index.php?go=animationen
    /// </summary>
    public class AnimationSystem : BasePlayerBehaviour
    {
#if UNITY_EDITOR
        // These properties are normally private. For the Debug Window in Editor Mode, we allow to read them.
        public List<AnimationTrackInstance> DebugTrackInstances => _trackInstances;
        public string[] DebugBoneNames => _boneNames;

        public bool DebugPauseAtPlayAnimation;
        public bool DebugPauseAtStopAnimation;
        public string DebugPlayAnimation;
#endif

        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly NpcService _npcService;


        public Transform RootBone;

        // Caching bone Transforms makes it faster to apply them to animations later.
        private string[] _boneNames;
        private Transform[] _bones;
        private Vector3[] _initialMeshBonePos;
        private Quaternion[] _initialMeshBoneRot;
        private List<AnimationTrackInstance> _trackInstances = new();
        private bool _isSittingInverted;

        // Some sitting animations are rotated wrong. They need to be inverted in y-axis.
        private string[] _animationsToInvertYAxis = { "S_BENCH_S1", "S_THRONE_S1" };


        // Attack information
        private bool IsAttack => AttackAnimation.NotNullOrEmpty();
        private string AttackAnimation;
        private string AttackHitLimb;
        private List<int> AttackOptFrame;
        private List<int> AttackHitEnd;
        private List<int> AttackWindowFrames;


        protected override void Awake()
        {
            base.Awake();

            // Cached object which will be used later.
            NpcData.PrefabProps.AnimationSystem = this;
        }

        private void Start()
        {
            Dictionary<string, Transform> bones = new();
            CollectBones(RootBone, bones);

            _boneNames = bones.Keys.ToArray();
            _bones = bones.Values.ToArray();
            _initialMeshBonePos = _bones.Select(i => i.transform.localPosition).ToArray();
            _initialMeshBoneRot = _bones.Select(i => i.transform.localRotation).ToArray();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (DebugPlayAnimation.NotNullOrEmpty())
                PlayAnimation(DebugPlayAnimation);
        }
#endif

        public void DisableObject()
        {
            // If an NPC is culled out, the old positions are still set. We need to reset them to ensure we have an idle NPC starting.
            for (var i = 0; i < _bones.Length; i++)
            {
                _bones[i].SetLocalPositionAndRotation(_initialMeshBonePos[i], _initialMeshBoneRot[i]);
            }

            DisableAttack();
            _trackInstances.Clear();
        }

        private void CollectBones(Transform bone, Dictionary<string, Transform> bones)
        {
            // Bones always start with BIP01. Other elements are Prefab specific.
            if (bone.name.StartsWith("BIP01") || bone.name.StartsWith("ZS_"))
            {
                bones.Add(bone.name, bone);
            }

            foreach (Transform child in bone)
            {
                CollectBones(child, bones);
            }
        }

        public bool PlayAnimation(string animationName)
        {
#if UNITY_EDITOR
            if (DebugPauseAtPlayAnimation)
            {
                Logger.LogEditor($"[Break] PlayAnimation: >{animationName}< on >{PrefabProps.Bip01.parent.parent.name}<", LogCat.Debug);
                Debug.Break();
            }
#endif

            var newTrack = _animationService.GetTrack(animationName, Properties.MdsNameBase, Properties.MdsNameOverlay);

            if (newTrack == null)
            {
                Logger.LogWarning($"Animation {animationName} not found and therefore can't be played.", LogCat.Animation);
                return false;
            }

            // FIXME - Now we need to handle animation flags: M - Move and R - Rotate.
            //         Then S_ROTATEL will work properly and stop once rotated enough.
            Logger.LogEditor($"Playing animation: {newTrack.Name}, alias: {newTrack.AliasName ?? "-"} by: {RootBone.parent.parent.name}", LogCat.Animation);

            if (IsAlreadyPlaying(newTrack))
                return true;

            var newTrackInstance = new AnimationTrackInstance(newTrack);
            var newTrackLayer = newTrackInstance.Track.Layer;

            // Handle existing Track Blending based on layer of new Track.
            for (var i = 0; i < _trackInstances.Count; i++)
            {
                var trackInstance = _trackInstances[i];
                var trackLayer = trackInstance.Track.Layer;

                if (trackLayer < newTrackLayer)
                {
                    BlendOutTrackBones(trackInstance, newTrackInstance);
                }
                else if (trackLayer == newTrackLayer)
                {
                    BlendOutTrack(trackInstance, newTrackInstance);
                }
                else if (trackLayer > newTrackLayer)
                {
                    StopTrackBones(trackInstance, newTrackInstance);
                }
            }

            PrePlayAnimation(newTrackInstance);
            _trackInstances.Add(newTrackInstance);

            // As Blending isn't always 1f at each time, we ensure some smoothness by sorting the TrackInstances like:
            // ORDER BY Track.Layer DESC AND Instance.CreationTime DESC
            // Newer (higher) CreationTime has higher precedence and will "forcefully" turn down the older animation on same layer.
            _trackInstances.Sort((instanceA, instanceB) =>
            {
                var layerComparison = instanceB.Track.Layer.CompareTo(instanceA.Track.Layer); // DESC
                return layerComparison != 0 ? layerComparison : instanceB.CreationTime.CompareTo(instanceA.CreationTime); // DESC
            });

            return true;
        }

        private bool IsAlreadyPlaying(AnimationTrack newTrack)
        {
            for (var i = 0; i < _trackInstances.Count; i++)
            {
                if (newTrack.IsSameAnimation(_trackInstances[i].Track))
                {
                    // e.g., t_warn might be called in parallel, when one warning is currently fading out.
                    if (_trackInstances[i].State == AnimationState.Play ||
                        _trackInstances[i].State == AnimationState.BlendIn)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool PlayIdleAnimation()
        {
            return PlayAnimation(_animationService.GetAnimationName(VmGothicEnums.AnimationType.Idle, NpcData));
        }

        public float GetAnimationDuration(string animationName)
        {
            for (var i = 0; i < _trackInstances.Count; i++)
            {
                var instance = _trackInstances[i];
                if (instance.Track.Name.EqualsIgnoreCase(animationName) || instance.Track.AliasName.EqualsIgnoreCase(animationName))
                {
                    return instance.Track.Duration;
                }
            }

            return 0f;
        }

        public void StopAnimation(string animationName)
        {
#if UNITY_EDITOR
            if (DebugPauseAtStopAnimation)
            {
                Logger.LogEditor($"[Break] StopAnimation: >{animationName}< on >{PrefabProps.Bip01.parent.parent.name}<", LogCat.Debug);
                Debug.Break();
            }
#endif

            var trackToStop = _animationService.GetTrack(animationName, Properties.MdsNameBase, Properties.MdsNameOverlay);
            
            Logger.LogEditor($"Stopping animation: {animationName}", LogCat.Animation);
            AnimationTrackInstance instanceToStop = null;

            // Fetch and blend out Animation.
            for (var i = 0; i < _trackInstances.Count; i++)
            {
                var instance = _trackInstances[i];
                // If animation is found, then mark it as "BlendOut"
                if (instance.Track.Name.EqualsIgnoreCase(trackToStop.Name))
                {
                    instanceToStop = instance;
                    instance.BlendOutTrack(instance.Track.BlendOut);

                    if (AttackAnimation == trackToStop.Name)
                        AttackAnimation = null;
                    // Do not break. We could potentially need to stop multiple instances of the same animation.
                }
            }

            if (instanceToStop == null)
            {
                return;
            }

            // Ramp up bones on animation with lower level as the higher level bones will blend out.
            for (var i = 0; i < _trackInstances.Count; i++)
            {
                var instance = _trackInstances[i];
                if (instance.Track.Name.EqualsIgnoreCase(animationName))
                {
                    continue;
                }

                // We BlendIn track bones from lower level animations, but only! if the animation isn't in a full-stop phase (!BlendOut/!Stop)
                if (instance.Track.Layer < instanceToStop.Track.Layer && (instance.State is AnimationState.BlendIn or AnimationState.Play))
                {
                    instance.BlendInBones(instanceToStop.Track.BoneNames, instanceToStop.Track.BlendOut);
                }
            }
        }

        /// <summary>
        /// Higher level Animations might have only a few bones which might be handled by a lower layer animation.
        /// We therefore need to blend out the other animation(s) Bones, not the whole animation.
        /// </summary>
        private void BlendOutTrackBones(AnimationTrackInstance lowerLayerTrack, AnimationTrackInstance higherLayerTrack)
        {
            lowerLayerTrack.BlendOutBones(higherLayerTrack.Track.BoneNames, higherLayerTrack.Track.BlendIn);
        }

        /// <summary>
        /// Tracks on the same layer will either need to stop immediately or blend out at the current frame.
        /// </summary>
        private void BlendOutTrack(AnimationTrackInstance oldTrack, AnimationTrackInstance newTrack)
        {
            // From Documentation:
            // E: Diese Flag sorgt dafür, dass die Ani erst gestartet wird, wenn eine zur Zeit aktive Ani im selben Layer ihren letzten Frame
            // erreicht hat und somit beendet wird. Sinnvoll z.B. in folgenden Fall: ani "s_walk", ani "t_walk_2_stand", ani "s_stand", wobei alle Anis als ASC-Anis vorliegen.
            var isStartAtLastFrame = newTrack.Track.Flags.HasFlag(AnimationFlags.Queue);

            if (isStartAtLastFrame)
            {
                // FIXME - Implement
                Logger.LogError("AnimationFlags.Queue not implemented yet.", LogCat.Animation);
            }
            // else
            // {
                oldTrack.BlendOutTrack(newTrack.Track.BlendIn);
            // }
        }

        /// <summary>
        /// The current instance starts blending out, which means, that lower layer bones can blend in again.
        /// </summary>
        private void BlendInOtherTrackBones(AnimationTrackInstance instanceBlendingOut)
        {
            foreach (var trackInstance in _trackInstances)
            {
                if (trackInstance.Track.Layer < instanceBlendingOut.Track.Layer)
                {
                    trackInstance.BlendInBones(instanceBlendingOut.Track.BoneNames, instanceBlendingOut.Track.BlendOut);
                }
            }
        }

        /// <summary>
        /// If we start a new instance, we need to apply, which bones should not be started, as e.g. T_DIALOGGESTURE_ from
        /// a higher level forced the animation to stop bones.
        /// </summary>
        private void StopTrackBones(AnimationTrackInstance lowerLayerTrack, AnimationTrackInstance higherLayerTrack)
        {
            var bonesToSkip = new List<string>();
            for (var i = 0; i < higherLayerTrack.Track.BoneCount; i++)
            {
                // TODO - If a higher layer bone is blending out, we should align lower level bone blend in times so that we have 1f weight at all time.
                // If the animation has bones in a BlendOut state, we do not skip them for our lower level animation.
                if (higherLayerTrack.BoneStates[i] == AnimationState.BlendOut)
                {
                    continue;
                }

                bonesToSkip.Add(higherLayerTrack.Track.BoneNames[i]);
            }

            lowerLayerTrack.BlendOutBones(bonesToSkip.ToArray(), 0f);
        }

        /// <summary>
        /// We need to ensure that we always have at least an idle animation running. Otherwise, e.g., a Wait(2) might cause an NPC after walking to not breathe.
        /// </summary>
        private void CheckAndSetIdleAnimation()
        {
            var hasLayer1AnimationRunning = false;
            foreach (var trackInstance in _trackInstances)
            {
                if (trackInstance.Track.Layer == 1 &&
                    trackInstance.State is AnimationState.BlendIn or AnimationState.Play)
                {
                    hasLayer1AnimationRunning = true;
                    break;
                }
            }

            if (!hasLayer1AnimationRunning)
                PlayIdleAnimation();
        }

        private void Update()
        {
            if (_trackInstances.Count == 0)
            {
                return;
            }

            // Update all tracks
            // ToArray() -> We need to copy the array, as we are modifying it.
            foreach (var instance in _trackInstances.ToArray())
            {
                switch (instance.Update(Time.deltaTime))
                {
                    case AnimationState.None:
                        break;
                    case AnimationState.BlendIn:
                        break;
                    case AnimationState.Play:
                        break;
                    case AnimationState.BlendOut:
                        if (instance.Track.NextAni.NotNullOrEmpty())
                        {
                            PlayAnimation(instance.Track.NextAni);
                        }
                        
                        BlendInOtherTrackBones(instance);
                        CheckAndSetIdleAnimation();
                        break;
                    case AnimationState.Stop:
                        PreStopAnimation(instance);
                        _trackInstances.Remove(instance);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            ApplyFinalPose();
            ApplyFinalMovement();
            ApplyFinalRotation();
            ApplyEvents();
        }

        private void PrePlayAnimation(AnimationTrackInstance instance)
        {
            if (_animationsToInvertYAxis.Contains(instance.Track.Name.ToUpper()))
                _isSittingInverted = true;
        }
        
        private void PreStopAnimation(AnimationTrackInstance instance)
        {
            if (_animationsToInvertYAxis.Contains(instance.Track.Name.ToUpper()))
                _isSittingInverted = false;

            if (AttackAnimation.EqualsIgnoreCase(instance.AnimationName))
                DisableAttack();
        }

        private void DisableAttack()
        {
            if (AttackAnimation == null)
                return;

            AttackAnimation = null;
            AttackHitLimb = null;
            AttackOptFrame = null;
            AttackHitEnd = null;
            AttackWindowFrames = null;

            // FIXME - We need to disable all limbs, if they are still active from current attack window.
        }

        private void ApplyFinalPose()
        {
            // Accumulate poses from all tracks
            for (var boneIndex = 0; boneIndex < _boneNames.Length; boneIndex++)
            {
                var boneName = _boneNames[boneIndex];
                var bone = _bones[boneIndex];

                var finalPosition = Vector3.zero;
                var finalRotation = Quaternion.identity;

                var boneWeightSum = 0f;
                for (var i = 0; i < _trackInstances.Count; i++)
                {
                    var trackInstance = _trackInstances[i];
                    var trackInstanceBoneIndex = trackInstance.GetBoneIndex(boneName);

                    // This track doesn't include the requested bone.
                    if (trackInstanceBoneIndex == -1)
                    {
                        continue;
                    }

                    var trackInstanceBoneWeight = trackInstance.BoneBlendWeights[trackInstanceBoneIndex];
                    boneWeightSum += trackInstanceBoneWeight;

                    // If we have some fast-changing situations like T_DIALOGGESTURE_ is blending out and another one is blending in - in between,
                    // We need to dynamically handle overweighting. We do so by reducing weights on lower layers
                    // (e.g. in the DIALOG case, T_WALK would be blended out more, as we sort the trackInstance list from high to low layer).
                    if (boneWeightSum > 1f)
                    {
                        // Fetch amount of weight higher than 1f on boneWeightSum
                        var amountOfOverWeight = boneWeightSum - 1f;
                        boneWeightSum = 1f;

                        trackInstanceBoneWeight -= amountOfOverWeight;
                    }

                    trackInstance.GetBonePose(trackInstanceBoneIndex, out var position, out var rotation);


                    finalPosition += position * trackInstanceBoneWeight;

                    // The first animation for a bone will define the start point of the rotation. Starting with Q.Identity is wrong and causes hickups.
                    if (i == 0)
                        finalRotation = rotation;
                    else
                        finalRotation = Quaternion.Slerp(finalRotation, rotation, trackInstanceBoneWeight);
                }

                // If we under blended the current object, we need to apply positions from the mesh itself.
                // Otherwise, we might have some 0.1f weight of animations alone and the NPC will implode like a black hole at 0,0,0.
                // This should be a rare case where we won't have a sum of 1.0. Just a safety treatment.
                if (boneWeightSum < 1f)
                {
                    finalPosition += _initialMeshBonePos[boneIndex] * (1 - boneWeightSum);
                    finalRotation = Quaternion.Slerp(finalRotation, _initialMeshBoneRot[boneIndex], 1 - boneWeightSum);
                }

                bone.localPosition = finalPosition;
                bone.localRotation = finalRotation;
            }
        }

        private void ApplyFinalMovement()
        {
            var finalMovement = Vector3.zero;
            for (var i = 0; i < _trackInstances.Count; i++)
            {
                var trackInstance = _trackInstances[i];

                if (!trackInstance.Track.IsMoving)
                    continue;

                // Stop, if we have no Root bone
                var boneIndex = trackInstance.GetBoneIndex(Constants.Animations.RootBoneName);
                if (boneIndex == -1)
                    continue;

                finalMovement += trackInstance.Track.MovementSpeed * trackInstance.BoneBlendWeights[boneIndex] * Time.deltaTime;
            }

            // Pos change is applied with rotated value.
            Go.transform.localPosition += Go.transform.rotation * finalMovement;
        }

        private void ApplyFinalRotation()
        {
            if (!_isSittingInverted)
                return;

            var currentRotation = PrefabProps.Bip01.transform.localRotation.eulerAngles;
            PrefabProps.Bip01.transform.localRotation = Quaternion.Euler(currentRotation.x, -currentRotation.y, currentRotation.z);
        }

        private void ApplyEvents()
        {
            for (var i = 0; i < _trackInstances.Count; i++)
            {
                var trackInstance = _trackInstances[i];

                ApplyEventTags(trackInstance);
                ApplySfxEvents(trackInstance);
                ApplyPfxEvents(trackInstance);
                ApplyMorphEvents(trackInstance);
            }
        }

        private void ApplyEventTags(AnimationTrackInstance trackInstance)
        {
            var eventTags = trackInstance.GetPendingEventTags();
            if (eventTags == null)
            {
                return;
            }

            foreach (var eventTag in eventTags)
            {
                switch (eventTag.Type)
                {
                    case EventType.ItemInsert:
                        _npcService.InsertItem(NpcData, eventTag.Slots.Item1, eventTag.Slots.Item2);
                        break;
                    case EventType.ItemDestroy:
                    case EventType.ItemRemove:
                        RemoveItem();
                        break;
                    case EventType.TorchInventory:
                        // TODO - I assume this means: if torch is in inventory, then put it out. But not really sure. Need a NPC with real usage of it to predict right.
                        break;
                    case EventType.HitLimb:
                        AttackHitLimb = eventTag.Slots.Item1;
                        AttackAnimation = trackInstance.AnimationName;
                        break;
                    case EventType.OptimalFrame:
                        AttackOptFrame = eventTag.Slots.Item1.Split(' ').Select(i => Convert.ToInt32(i)).ToList();
                        break;
                    case EventType.HitEnd:
                        AttackHitEnd = eventTag.Slots.Item1.Split(' ').Select(i => Convert.ToInt32(i)).ToList();
                        break;
                    case EventType.ComboWindow:
                        AttackWindowFrames = eventTag.Slots.Item1.Split(' ').Select(i => Convert.ToInt32(i)).ToList();
                        break;
                    // Unused. @see: https://gothic-modding-community.github.io/gmc/zengin/anims/events/#def_dir
                    case EventType.HitDirection:
                        break;
                    default:
                        Logger.LogWarning($"EventType.type {eventTag.Type} not yet supported.", LogCat.Animation);
                        break;
                }
            }
        }

        private void ApplySfxEvents(AnimationTrackInstance trackInstance)
        {
            var sfxEvents = trackInstance.GetPendingSoundEffects();
            if (sfxEvents == null)
                return;

            foreach (var sfx in sfxEvents)
            {
                var clip = _audioService.GetRandomSoundClip(sfx.Name);
                PrefabProps.NpcSound.clip = clip;
                PrefabProps.NpcSound.maxDistance = sfx.Range.ToMeter();
                PrefabProps.NpcSound.Play();
            }
        }

        private void ApplyPfxEvents(AnimationTrackInstance trackInstance)
        {
            var pfxEvents = trackInstance.GetPendingParticleEffects();
            if (pfxEvents == null)
                return;

            foreach (var pfx in pfxEvents)
            {
                Logger.LogWarning($"Particle Effects are not yet supported. {pfx.Name}", LogCat.Animation);
            }
        }

        private void ApplyMorphEvents(AnimationTrackInstance trackInstance)
        {
            var morphEvents = trackInstance.GetPendingMorphAnimations();
            if (morphEvents == null)
                return;

            foreach (var morph in morphEvents)
            {
                var type = PrefabProps.HeadMorph.GetAnimationTypeByName(morph.Animation);

                PrefabProps.HeadMorph.StartAnimation(Properties.BodyData.Head, type);
            }
        }

        private void RemoveItem()
        {
            // Some animations need to force remove items, some not.
            if (Properties.UsedItemSlot == "")
            {
                return;
            }

            var slotGo = PrefabProps.Bip01.FindChildRecursively(Properties.UsedItemSlot);
            var item = slotGo!.GetChild(0);

            Destroy(item.gameObject);
        }

        public void StopAllAnimations()
        {
            DisableObject();
        }

        public void PlayHeadAnimation(HeadMorph.HeadMorphType viseme)
        {
            // FIXME - Implement
            Logger.LogWarning("PlayHeadAnimation not yet implemented.", LogCat.Animation);
        }

        public void StopHeadAnimation(HeadMorph.HeadMorphType viseme)
        {
            // FIXME - Implement
            Logger.LogWarning("StopHeadAnimation not yet implemented.", LogCat.Animation);
        }

        public bool IsPlaying(string animationName)
        {
            foreach (var trackInstance in _trackInstances)
            {
                if (trackInstance.Track.Name.EqualsIgnoreCase(animationName))
                    return true;
            }
            
            return false;
        }
    }
}
