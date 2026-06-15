using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Animations.Morph;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Animations;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using MyBox;
using Reflex.Attributes;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using ZenKit;
using AnimationState = Gothic.Core.Models.Animations.AnimationState;
using EventType = ZenKit.EventType;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Animations
{
    /// <summary>
    /// NPC component to handle animations. The Blending is using the official Gothic animation information:
    /// https://www.worldofgothic.de/modifikation/index.php?go=animationen
    ///
    /// Gothic's layer/blend model is executed by AnimationPoseJob inside a per-NPC PlayableGraph: the job
    /// samples the baked .man data and applies tracks in (Gothic layer ASC, creation time ASC) order, each
    /// overriding exactly the bones it drives at its blend weight, on top of the skeleton rest pose.
    /// This class manages the Gothic runtime state feeding that job: playback clocks, blend-weight ramps,
    /// animation events, root motion, and idle fallback.
    /// </summary>
    public class AnimationSystem : BasePlayerBehaviour
    {
#if UNITY_EDITOR
        // These properties are normally private. For the Debug Window in Editor Mode, we allow to read them.
        public List<AnimationTrackInstance> DebugTrackInstances => _trackInstances;

        public bool DebugPauseAtPlayAnimation;
        public bool DebugPauseAtStopAnimation;
        public string DebugPlayAnimation;
#endif

        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly NpcService _npcService;


        // Initial bone pose is needed to reset culled-out NPCs to an idle starting state.
        private Transform[] _bones;
        private Vector3[] _initialMeshBonePos;
        private Quaternion[] _initialMeshBoneRot;

        private Animator _animator;
        private PlayableGraph _graph;
        private AnimationScriptPlayable _posePlayable;
        private AnimationSkeleton _skeleton;
        // Stream handles to the bone transforms, index == mdh node index (matches AnimationTrack.BoneToNode).
        private NativeArray<TransformStreamHandle> _handles;
        // Pose of the previous job evaluation (initialized with the rest pose). Bones without an active track
        // keep their last pose (Gothic behavior) instead of snapping back to rest - e.g. s_Bench_S1 doesn't
        // drive BIP01 and relies on the height the sit-down transition left it at.
        private NativeArray<Vector3> _posePositions;
        private NativeArray<Quaternion> _poseRotations;
        // Reusable buffer for the per-frame job weights (see CalculateSlotWeights).
        private float[] _slotWeights = new float[AnimationPoseJob.MaxTracks];

        // Walk capsule following the animated root height (see UpdateRootCollider).
        private CapsuleCollider _walkCapsule;
        private float _walkCapsuleBaseRadius;
        private float _restRootHeight;
        private Transform _rootBone;
        private float _appliedRootHeightOffset;
        // Re-size only on real pose changes (kneeling, flying, jumps) - not for the few-cm bob of walk cycles.
        private const float _rootColliderUpdateThreshold = 0.05f;

        private List<AnimationTrackInstance> _trackInstances = new();
        // Reusable snapshot for Update(): instances can be added (NextAni/idle) or removed while iterating.
        private List<AnimationTrackInstance> _updateSnapshot = new();
        private bool _isSittingInverted;
        private Quaternion _lastInvertedRotation;

        // Cached to avoid a delegate allocation per PlayAnimation call.
        private static readonly Comparison<AnimationTrackInstance> _trackOrderComparison = (instanceA, instanceB) =>
        {
            var layerComparison = instanceA.Track.Layer.CompareTo(instanceB.Track.Layer);
            return layerComparison != 0 ? layerComparison : instanceA.CreationTime.CompareTo(instanceB.CreationTime);
        };


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
            var bones = new List<Transform>();
            // Collect from the NPC root, not RootBone: some skeletons (e.g. Bloodfly's "BIP01 CENTER") are
            // created as siblings of the prefab's BIP01 and would be missed otherwise.
            CollectBones(Go.transform, bones);

            _bones = bones.ToArray();
            _initialMeshBonePos = _bones.Select(i => i.localPosition).ToArray();
            _initialMeshBoneRot = _bones.Select(i => i.localRotation).ToArray();

            CreateGraph();
            ResizeRootCollider();
        }

        /// <summary>
        /// Every clip pins the skeleton root to local zero horizontally (vertically it poses an offset around
        /// the rest height), so physics decides how high the NPC stands: it settles where the walk capsule
        /// touches the ground. The capsule (reparented under the NPC root by RootCollisionHandler) must
        /// therefore end exactly at foot level = RootTranslation.y below the NPC root. The prefab default
        /// (1m, human-sized) makes smaller skeletons like Molerat or Gobbo hover above the ground.
        /// </summary>
        private void ResizeRootCollider()
        {
            var rootHeight = _animationService.GetRootBoneHeight(Properties.MdsNameBase);
            var colliderTransform = PrefabProps.ColliderRootMotion;

            if (rootHeight <= 0f || colliderTransform == null ||
                !colliderTransform.TryGetComponent<CapsuleCollider>(out var capsule))
            {
                return;
            }

            _walkCapsule = capsule;
            _restRootHeight = rootHeight;
            // Unity clamps height to 2*radius, so the radius is reduced for skeletons smaller than the capsule.
            _walkCapsuleBaseRadius = Mathf.Min(capsule.radius, rootHeight);

            UpdateRootCollider(0f);
        }

        /// <summary>
        /// Follow the animated root height with the walk capsule. All values are local to the NPC root (the
        /// capsule's parent): the bottom always stays at foot level (physics settles the NPC on it - it must
        /// not move, or the NPC would re-settle), while the top tracks the root bone's baked Y offset:
        /// kneeling (s_Pray) or sitting poses shrink the capsule, flying (Bloodfly) or jumping raises it.
        /// offset == 0 yields the rest pose capsule, symmetric around the root bone's rest height
        /// (identical to the human prefab: 1m radius around BIP01).
        /// </summary>
        private void UpdateRootCollider(float rootHeightOffset)
        {
            var bottom = -_restRootHeight;
            var top = _restRootHeight + rootHeightOffset;

            // Lying poses can push the root (almost) to the ground - keep a minimal cylinder for collisions.
            var minHeight = Mathf.Min(0.2f, _restRootHeight);
            var height = Mathf.Max(top - bottom, minHeight);

            _walkCapsule.radius = Mathf.Min(_walkCapsuleBaseRadius, height / 2f);
            _walkCapsule.height = height;
            _walkCapsule.center = new Vector3(0f, bottom + height / 2f, 0f);

            _appliedRootHeightOffset = rootHeightOffset;
        }

        /// <summary>
        /// The animated root height is only known after the Animator wrote the pose, i.e. in LateUpdate().
        /// </summary>
        private void FollowRootColliderHeight()
        {
            if (_walkCapsule == null || _rootBone == null)
                return;

            var rootHeightOffset = _rootBone.localPosition.y;
            if (Mathf.Abs(rootHeightOffset - _appliedRootHeightOffset) < _rootColliderUpdateThreshold)
                return;

            UpdateRootCollider(rootHeightOffset);
        }

        /// <summary>
        /// The graph is created once the bone GameObjects exist (mesh builders run before Start()), as the
        /// stream handles bind directly to the bone transforms.
        /// </summary>
        private void CreateGraph()
        {
            if (_graph.IsValid())
            {
                return;
            }

            _skeleton = _animationService.GetSkeleton(Properties.MdsNameBase);
            if (_skeleton == null)
            {
                Logger.LogError($"No model hierarchy found for >{Properties.MdsNameBase}< - animations are disabled on {Go.name}.", LogCat.Animation);
                return;
            }

            _animator = gameObject.TryGetComponent<Animator>(out var existingAnimator)
                ? existingAnimator
                : gameObject.AddComponent<Animator>();
            _animator.applyRootMotion = false; // Root motion is applied manually (see ApplyFinalMovement).
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // NPC culling is handled by our own culling domain.

            _graph = PlayableGraph.Create($"AnimationSystem-{Go.name}");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            BindBones();

            _posePositions = new NativeArray<Vector3>(_skeleton.RestPositions, Allocator.Persistent);
            _poseRotations = new NativeArray<Quaternion>(_skeleton.RestRotations, Allocator.Persistent);

            _posePlayable = AnimationScriptPlayable.Create(_graph, BuildJobData());

            var output = AnimationPlayableOutput.Create(_graph, "Animation", _animator);
            output.SetSourcePlayable(_posePlayable);

            _graph.Play();
        }

        private void BindBones()
        {
            _handles = new NativeArray<TransformStreamHandle>(_skeleton.NodeCount, Allocator.Persistent);

            for (var node = 0; node < _skeleton.NodeCount; node++)
            {
                var boneTransform = transform.Find(_skeleton.Paths[node]);
                if (boneTransform == null)
                {
                    // The job skips default handles (IsValid() == false).
                    Logger.LogWarning($"Bone >{_skeleton.Paths[node]}< not found below {Go.name} - it won't be animated.", LogCat.Animation);
                    continue;
                }

                if (node == _skeleton.RootNodeIndex)
                {
                    // The walk capsule follows this bone's animated height (see FollowRootColliderHeight).
                    _rootBone = boneTransform;
                }

                _handles[node] = _animator.BindStreamTransform(boneTransform);
            }
        }

        /// <summary>
        /// Snapshot of all active tracks for AnimationPoseJob. Rebuilt (cheap struct copy) every frame, as
        /// playback clocks and blend weights change constantly. Unused slots get filler arrays - the job's
        /// safety system requires created arrays even when TrackCount keeps them untouched.
        /// </summary>
        private AnimationPoseJob BuildJobData()
        {
            var job = new AnimationPoseJob
            {
                Handles = _handles,
                PosePositions = _posePositions,
                PoseRotations = _poseRotations,
                TrackCount = Mathf.Min(_trackInstances.Count, AnimationPoseJob.MaxTracks)
            };

            // If we ever exceed the slots, drop the lowest layers - they'd be overridden by the higher ones anyway.
            var firstInstance = _trackInstances.Count - job.TrackCount;

            CalculateSlotWeights(firstInstance, job.TrackCount);

            for (var slot = 0; slot < job.TrackCount; slot++)
            {
                var instance = _trackInstances[firstInstance + slot];
                var track = instance.Track;

                job.SetTrack(slot, new AnimationPoseJobTrack
                {
                    Frame = instance.CurrentFrame,
                    FrameCount = track.BakedFrameCount,
                    BoneCount = track.BoneCount,
                    Weight = _slotWeights[slot]
                }, track.Positions, track.Rotations, track.BoneToNode);
            }

            for (var slot = job.TrackCount; slot < AnimationPoseJob.MaxTracks; slot++)
            {
                job.SetTrack(slot, default, _skeleton.RestPositions, _skeleton.RestRotations, _skeleton.EmptyBoneMap);
            }

            return job;
        }

        /// <summary>
        /// The job applies slots sequentially (lerp over the result so far). For a same-layer crossfade
        /// (A blending out while B blends in, Gothic weights summing to ~1) a naive sequential application
        /// would leave A only weightA * (1 - weightB) and let the rest pose bleed through.
        /// Boost earlier same-layer slots so their final contribution matches their Gothic weight:
        /// effective = weight / (1 - sum of later same-layer weights). Across layers the raw weight is kept,
        /// as higher layers intentionally override lower ones.
        /// </summary>
        private void CalculateSlotWeights(int firstInstance, int trackCount)
        {
            var layerTailWeight = 0f;

            for (var slot = trackCount - 1; slot >= 0; slot--)
            {
                var instance = _trackInstances[firstInstance + slot];

                var isSameLayerAsNext = slot < trackCount - 1 &&
                                        _trackInstances[firstInstance + slot + 1].Track.Layer == instance.Track.Layer;
                if (!isSameLayerAsNext)
                {
                    layerTailWeight = 0f;
                }

                _slotWeights[slot] = layerTailWeight >= 1f
                    ? 0f // Fully covered by newer animations on the same layer.
                    : Mathf.Min(1f, instance.Weight / (1f - layerTailWeight));

                layerTailWeight += instance.Weight;
            }
        }

        private void UpdateJobData()
        {
            if (_graph.IsValid())
            {
                _posePlayable.SetJobData(BuildJobData());
            }
        }

        private void OnDestroy()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            if (_handles.IsCreated)
            {
                _handles.Dispose();
            }

            if (_posePositions.IsCreated)
            {
                _posePositions.Dispose();
                _poseRotations.Dispose();
            }
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
            _trackInstances.Clear();

            // Forget the last animated pose - the NPC restarts from an idle rest state when culled in again.
            if (_posePositions.IsCreated)
            {
                _posePositions.CopyFrom(_skeleton.RestPositions);
                _poseRotations.CopyFrom(_skeleton.RestRotations);
            }

            UpdateJobData();

            // If an NPC is culled out, the old positions are still set. We need to reset them to ensure we have an idle NPC starting.
            for (var i = 0; i < _bones.Length; i++)
            {
                _bones[i].SetLocalPositionAndRotation(_initialMeshBonePos[i], _initialMeshBoneRot[i]);
            }

            // The bones are back at the rest pose, so the walk capsule needs to match it again
            // (LateUpdate won't run while the NPC is culled out).
            if (_walkCapsule != null)
            {
                UpdateRootCollider(0f);
            }

            DisableAttack();
        }

        private void CollectBones(Transform bone, List<Transform> bones)
        {
            // Bones always start with BIP01. Other elements are Prefab specific.
            if (bone.name.StartsWith("BIP01") || bone.name.StartsWith("ZS_"))
            {
                bones.Add(bone);
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

            Logger.LogEditor($"Playing animation: {newTrack.Name}, alias: {newTrack.AliasName ?? "-"} by: {Go.name}", LogCat.Animation);

            if (IsAlreadyPlaying(newTrack))
                return true;

            // Tracks on the same layer blend out with the BlendIn time of the new track.
            // Lower/higher layer interplay needs no special handling: AnimationPoseJob applies higher layers
            // over lower ones for exactly the bones they drive, and lower layers shine through again on blend out.
            foreach (var instance in _trackInstances)
            {
                if (instance.Track.Layer == newTrack.Layer)
                {
                    // From Documentation:
                    // E: Diese Flag sorgt dafür, dass die Ani erst gestartet wird, wenn eine zur Zeit aktive Ani im selben
                    // Layer ihren letzten Frame erreicht hat und somit beendet wird.
                    if (newTrack.Flags.HasFlag(AnimationFlags.Queue))
                    {
                        // FIXME - Implement
                        Logger.LogWarning("AnimationFlags.Queue not implemented yet.", LogCat.Animation);
                    }

                    instance.BlendOutTrack(newTrack.BlendIn);
                }
            }

            // PlayAnimation can be reached from another component's Start() before our own Start() ran.
            CreateGraph();

            var newInstance = new AnimationTrackInstance(newTrack);

            PrePlayAnimation(newInstance);
            _trackInstances.Add(newInstance);

            // AnimationPoseJob applies tracks in list order: later entries override earlier ones (for their bones).
            // ORDER BY Track.Layer ASC, Instance.CreationTime ASC --> higher Gothic layers and newer instances win.
            _trackInstances.Sort(_trackOrderComparison);

            if (_trackInstances.Count > AnimationPoseJob.MaxTracks)
            {
                Logger.LogWarning($"More than {AnimationPoseJob.MaxTracks} animations playing on {Go.name} - the lowest layers are skipped.", LogCat.Animation);
            }

            return true;
        }

        private bool IsAlreadyPlaying(AnimationTrack newTrack)
        {
            foreach (var instance in _trackInstances)
            {
                if (newTrack.IsSameAnimation(instance.Track))
                {
                    // e.g., t_warn might be called in parallel, when one warning is currently fading out.
                    if (instance.State == AnimationState.Play ||
                        instance.State == AnimationState.BlendIn)
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
            foreach (var instance in _trackInstances)
            {
                if (instance.Track.MatchesName(animationName))
                {
                    return instance.Track.Duration;
                }
            }

            // Not playing right now - resolve via track cache instead of returning a bogus 0-duration.
            var track = _animationService.GetTrack(animationName, Properties.MdsNameBase, Properties.MdsNameOverlay);
            return track?.Duration ?? 0f;
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

            Logger.LogEditor($"Stopping animation: {animationName}", LogCat.Animation);

            foreach (var instance in _trackInstances)
            {
                if (!instance.Track.MatchesName(animationName))
                {
                    continue;
                }

                instance.BlendOutTrack(instance.Track.BlendOut);

                if (instance.Track.MatchesName(AttackAnimation))
                    AttackAnimation = null;
                // Do not break. We could potentially need to stop multiple instances of the same animation.
            }
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

            // Iterate over a snapshot: NextAni chaining and the idle fallback add new instances (and re-sort) while we loop.
            _updateSnapshot.Clear();
            _updateSnapshot.AddRange(_trackInstances);
            foreach (var instance in _updateSnapshot)
            {
                switch (instance.Update(Time.deltaTime))
                {
                    case AnimationState.None:
                    case AnimationState.BlendIn:
                    case AnimationState.Play:
                        break;
                    case AnimationState.BlendOut:
                        if (instance.Track.NextAni.NotNullOrEmpty())
                        {
                            PlayAnimation(instance.Track.NextAni);
                        }

                        CheckAndSetIdleAnimation();
                        break;
                    case AnimationState.Stop:
                        PreStopAnimation(instance);
                        _trackInstances.Remove(instance);

                        // Externally stopped tracks (e.g. AI_StopAni, end of a walk) never pass through the
                        // BlendOut case above. Without this check an NPC whose last animation was stopped
                        // would freeze in the rest pose instead of falling back to its breathing idle.
                        CheckAndSetIdleAnimation();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // Feed the updated clocks and blend weights into the animation job. Posing itself happens there.
            UpdateJobData();

            ApplyFinalMovement();
            ApplyEvents();
        }

        /// <summary>
        /// The Animator evaluates after Update() and would overwrite transform changes made there.
        /// Pose post-processing therefore needs to happen in LateUpdate().
        /// </summary>
        private void LateUpdate()
        {
            FollowRootColliderHeight();
            ApplyFinalRotation();
        }

        private void PrePlayAnimation(AnimationTrackInstance instance)
        {
            if (instance.Track.InvertYAxis)
                _isSittingInverted = true;
        }

        private void PreStopAnimation(AnimationTrackInstance instance)
        {
            if (instance.Track.InvertYAxis)
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

        private void ApplyFinalMovement()
        {
            var finalMovement = Vector3.zero;
            foreach (var instance in _trackInstances)
            {
                // Only movement tracks (walk, run, strafe) translate the NPC. Vertical pose motion
                // (fly height, sitting down, jump arcs) is baked into the root bone's Y channel instead.
                // During blend-out, root motion stops to prevent residual sliding
                // (e.g. NPC sliding forward when walk is replaced by a turn animation).
                if (!instance.Track.IsMoving || instance.State == AnimationState.BlendOut)
                    continue;

                finalMovement += instance.Track.MovementSpeed * Time.deltaTime;
            }

            // Pos change is applied with rotated value.
            Go.transform.localPosition += Go.transform.rotation * finalMovement;
        }

        private void ApplyFinalRotation()
        {
            if (!_isSittingInverted)
                return;

            var bip01 = PrefabProps.Bip01.transform;

            // Only invert poses freshly written by the Animator. If the rotation still holds our own last write
            // (i.e. no clip drove the bone this frame), inverting again would flip-flop the NPC every frame.
            if (bip01.localRotation == _lastInvertedRotation)
                return;

            var currentRotation = bip01.localRotation.eulerAngles;
            _lastInvertedRotation = Quaternion.Euler(currentRotation.x, -currentRotation.y, currentRotation.z);
            bip01.localRotation = _lastInvertedRotation;
        }

        private void ApplyEvents()
        {
            for (var i = 0; i < _trackInstances.Count; i++)
            {
                var trackInstance = _trackInstances[i];

                if (!trackInstance.Track.HasEvents)
                    continue;

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
                        AttackOptFrame = eventTag.Slots.Item1.Split(' ').Where(s => !string.IsNullOrEmpty(s)).Select(i => Convert.ToInt32(i)).ToList();
                        break;
                    case EventType.HitEnd:
                        AttackHitEnd = eventTag.Slots.Item1.Split(' ').Where(s => !string.IsNullOrEmpty(s)).Select(i => Convert.ToInt32(i)).ToList();
                        break;
                    case EventType.ComboWindow:
                        AttackWindowFrames = eventTag.Slots.Item1.Split(' ').Where(s => !string.IsNullOrEmpty(s)).Select(i => Convert.ToInt32(i)).ToList();
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

        /// <summary>
        /// Returns true when the named animation track has entered its blend-out phase
        /// ahead of its natural end — i.e. it was stopped externally (e.g. AI_StopAni).
        /// The owning action can detect this and finish itself without waiting for the
        /// full duration timer to expire.
        /// </summary>
        public bool IsAnimationBlendingOut(string animationName)
        {
            foreach (var instance in _trackInstances)
            {
                if (instance.Track.MatchesName(animationName)
                    && instance.State == AnimationState.BlendOut)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
