using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Animations.Morph;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Animations;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
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
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly NpcWaterService _npcWaterService;
        [Inject] private readonly Gothic.Core.Services.Meshes.MeshService _meshService;
        [Inject] private readonly Gothic.Core.Services.Caches.VmCacheService _vmCacheService;

        // Item states (smoking, drinking): the engine's random item animations every few seconds.
        private const float _itemRandomAniMinSeconds = 6f;
        private const float _itemRandomAniMaxSeconds = 14f;
        private const int _maxItemRandomAnis = 4;
        private float _nextItemRandomAniTime;

        // *eventPFX effects that run until their *eventPFXStop (same index). Safety: gone after this time anyway.
        private readonly Dictionary<int, GameObject> _activePfx = new();
        private const float _pfxMaxSeconds = 12f;
        private const float _pfxFadeSeconds = 8f;


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
        // Height of the root bone above the feet in the rest pose (NpcGo sits at feet + this).
        public float RestRootHeight => _restRootHeight;
        private Transform _rootBone;
        private float _appliedRootHeightOffset;
        // Re-size only on real pose changes (kneeling, flying, jumps) - not for the few-cm bob of walk cycles.
        private const float _rootColliderUpdateThreshold = 0.05f;
        private float _lastMovementLogTime = -999f;

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
            // Snap NpcGo to ground now that we know restRootHeight, then allow gravity.
            // RootCollisionHandler.Awake() set kinematic=true to block the depenetration jump
            // that would otherwise happen before this point.
            SnapToGround();
            var parentTf = Go.transform.parent;
            var parentInfo = parentTf != null ? $"parent='{parentTf.name}' parentPos={parentTf.position}" : "parent=none";
            Logger.Log($"[SnapDiag] {Go.name} Start: enabling physics. GoWorldPos={Go.transform.position} GoLocalPos={Go.transform.localPosition} {parentInfo}", LogCat.Animation);
            if (PrefabProps.ColliderRootMotion != null)
                PrefabProps.ColliderRootMotion.GetComponent<Rigidbody>().isKinematic = false;
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
            // The overlay MDH defines the actual bone hierarchy used by the mesh builder, so its
            // RootTranslation.y is the true rest height of the root bone above the feet. Using the
            // base MDH instead (e.g. humans.mdh for a skeleton NPC whose bones come from
            // humans_skeleton.mdh) sizes the capsule for the wrong skeleton, causing floating or
            // incorrect terrain contact.
            var mdsForHeight = string.IsNullOrEmpty(Properties.MdsNameOverlay)
                ? Properties.MdsNameBase
                : Properties.MdsNameOverlay;
            var rootHeight = _animationService.GetRootBoneHeight(mdsForHeight);
            if (rootHeight <= 0f)
                rootHeight = _animationService.GetRootBoneHeight(Properties.MdsNameBase);
            var colliderTransform = PrefabProps.ColliderRootMotion;

            Logger.Log($"[SnapDiag] {Go.name} ResizeRootCollider: base={Properties.MdsNameBase} overlay={Properties.MdsNameOverlay} mdsForHeight={mdsForHeight} rootHeight={rootHeight:F3} collider={(colliderTransform == null ? "NULL" : "OK")}", LogCat.Animation);

            if (rootHeight <= 0f || colliderTransform == null ||
                !colliderTransform.TryGetComponent<CapsuleCollider>(out var capsule))
            {
                Logger.LogWarning($"[SnapDiag] {Go.name} ResizeRootCollider SKIPPED: rootHeight={rootHeight:F3} colliderNull={colliderTransform == null}", LogCat.Animation);
                return;
            }

            _walkCapsule = capsule;
            _restRootHeight = rootHeight;
            // Unity clamps height to 2*radius, so the radius is reduced for skeletons smaller than the capsule.
            _walkCapsuleBaseRadius = Mathf.Min(capsule.radius, rootHeight);

            UpdateRootCollider(0f);
            Logger.Log($"[SnapDiag] {Go.name} ResizeRootCollider DONE: restRootHeight={_restRootHeight:F3} capsuleCenter={_walkCapsule.center} capsuleHeight={_walkCapsule.height:F3} capsuleLayer={colliderTransform.gameObject.layer}", LogCat.Animation);
        }

        /// Snap NpcGo.Y so the capsule bottom lands exactly on the world mesh below.
        /// Called by PhysicsService before enabling the rigidbody: if NpcGo was placed at terrain level
        /// (waypoint Y), the capsule bottom is 1 m underground, and physics depenetration would push the
        /// NPC into the air the moment gravity activates. The snap prevents that jump.
        public void SnapToGround()
        {
            if (_restRootHeight <= 0f || _walkCapsule == null)
            {
                Logger.LogWarning($"[SnapDiag] {Go.name} SnapToGround SKIPPED: restRootHeight={_restRootHeight:F3} walkCapsuleNull={_walkCapsule == null}", LogCat.Animation);
                return;
            }

            var groundMask = 1 << (int)Constants.DefaultLayer;
            var origin = Go.transform.position + Vector3.up * 2f;
            Logger.Log($"[SnapDiag] {Go.name} SnapToGround: GoWorldPos={Go.transform.position} GoLocalPos={Go.transform.localPosition} origin={origin} mask={groundMask}", LogCat.Animation);

            if (!Physics.Raycast(origin, Vector3.down, out var hit, 20f, groundMask))
            {
                Logger.LogWarning($"[SnapDiag] {Go.name} SnapToGround RAYCAST MISSED from {origin} (no ground within 20m on DefaultLayer)", LogCat.Animation);
                return;
            }

            var beforeY = Go.transform.position.y;
            var targetY = hit.point.y + _restRootHeight;
            Logger.Log($"[SnapDiag] {Go.name} SnapToGround HIT: collider='{hit.collider.name}' layer={hit.collider.gameObject.layer} hitY={hit.point.y:F3} restH={_restRootHeight:F3} targetY={targetY:F3} currentY={beforeY:F3} delta={targetY - beforeY:F3}", LogCat.Animation);

            if (Mathf.Abs(beforeY - targetY) < 0.05f)
            {
                Logger.Log($"[SnapDiag] {Go.name} SnapToGround: already at correct Y (delta < 0.05), no move needed", LogCat.Animation);
                return;
            }

            var pos = Go.transform.position;
            Go.transform.position = new Vector3(pos.x, targetY, pos.z);
            Logger.Log($"[SnapDiag] {Go.name} SnapToGround MOVED: {beforeY:F3} -> {targetY:F3}", LogCat.Animation);
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
            StopAllPfx();

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

        /// <summary>
        /// Only the idle loop runs (ignoring tracks blending out) - nothing to stop.
        /// </summary>
        public bool IsPlayingOnlyIdle()
        {
            var idleName = _animationService.GetAnimationName(VmGothicEnums.AnimationType.Idle, NpcData);
            var hasIdle = false;
            foreach (var instance in _trackInstances)
            {
                if (instance.State is AnimationState.BlendOut or AnimationState.Stop)
                    continue;
                if (!instance.Track.MatchesName(idleName))
                    return false;
                hasIdle = true;
            }
            return hasIdle;
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

            UpdateItemRandomAni();

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
            // DeveloperConfig.EnableMobSeatFix: the 2025 inversion turned seated NPCs around once the sit loop started
            // (the transition into it looks right) and could stay active after standing up (walking backwards).
            if (instance.Track.InvertYAxis && !_configService.Dev.EnableMobSeatFix)
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

            // Strip pitch/roll — Gothic waypoints can have non-zero X/Z rotation (sloped terrain).
            // Applying raw Go.transform.rotation to a horizontal movement vector produces a world-space
            // Y component equal to speed * sin(pitch), which floats the NPC upward while walking.
            var yawRotation = Quaternion.Euler(0f, Go.transform.eulerAngles.y, 0f);
            var worldMove = yawRotation * finalMovement;

            worldMove = SlideAlongWalls(worldMove);

            // Deep water stops non-swimmers, swimmers float at the surface (DeveloperConfig.EnableNpcWater).
            worldMove = _npcWaterService.ApplyWater(NpcData, worldMove, _restRootHeight,
                _rootBone != null ? _rootBone.localPosition.y : 0f);

            // Log if anything has Y — rate-limited to once per second.
            if ((Mathf.Abs(finalMovement.y) > 0.0001f || Mathf.Abs(worldMove.y) > 0.0001f)
                && Time.time - _lastMovementLogTime > 1f)
            {
                Logger.Log($"[SnapDiag] {Go.name} ApplyFinalMovement: rawY={finalMovement.y:F5} worldY={worldMove.y:F5} GoWorldY={Go.transform.position.y:F3}", LogCat.Animation);
                _lastMovementLogTime = Time.time;
            }

            Go.transform.localPosition += worldMove;
        }

        private const float _wallProbeRadius = 0.3f;
        private const float _wallMinNormalY = 0.5f;

        /// <summary>
        /// DeveloperConfig.EnableNpcWallCollision: root motion moves the NPC transform directly (kinematic while
        /// walking) - fast runners (scavengers) went into rocks and fell below the world. A sphere at hip height probes
        /// the move; against a wall only the part along the wall stays. Floors/slopes (normal up) don't block.
        /// </summary>
        private Vector3 SlideAlongWalls(Vector3 worldMove)
        {
            // Mob interactions (benches, beds, ...) walk the NPC into the mob on purpose.
            if (!_configService.Dev.EnableNpcWallCollision || _restRootHeight <= 0f || PrefabProps.CurrentInteractable != null)
                return worldMove;

            var horizontal = new Vector3(worldMove.x, 0f, worldMove.z);
            var distance = horizontal.magnitude;
            if (distance < 0.0001f)
                return worldMove;

            var radius = Mathf.Min(_wallProbeRadius, _restRootHeight * 0.5f);
            if (!Physics.SphereCast(Go.transform.position, radius, horizontal / distance, out var hit, distance + 0.05f,
                    1 << Constants.DefaultLayer, QueryTriggerInteraction.Ignore) || hit.normal.y >= _wallMinNormalY)
                return worldMove;

            var wallNormal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            var slide = Vector3.ProjectOnPlane(horizontal, wallNormal);
            return new Vector3(slide.x, worldMove.y, slide.z);
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
                        // ZenKit stores numeric frame params in .Frames, not .Slots (which holds string params like bone names).
                        AttackOptFrame = eventTag.Frames;
                        break;
                    case EventType.HitEnd:
                        AttackHitEnd = eventTag.Frames;
                        break;
                    case EventType.ComboWindow:
                        AttackWindowFrames = eventTag.Frames;
                        if (AttackWindowFrames.Count == 0)
                            Logger.LogWarning($"[ComboWindow] DEF_WINDOW on >{trackInstance.AnimationName}< has no frame params - combo window will never open.", LogCat.Animation);
                        else
                            Logger.Log($"[ComboWindow] DEF_WINDOW on >{trackInstance.AnimationName}< frames={string.Join(",", AttackWindowFrames)}", LogCat.Animation);
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

        /// <summary>
        /// DeveloperConfig.EnableItemRandomAnis: in an item state (AI_UseItemToState, e.g. s_JOINT_S0) the engine plays
        /// t_<SCHEME>_Random_1..n now and then - smoking NPCs take a drag (with its smoke PFX), drinkers sip.
        /// </summary>
        private void UpdateItemRandomAni()
        {
            if (!_configService.Dev.EnableItemRandomAnis || !Properties.HasItemEquipped ||
                Properties.CurrentItem < 0 || Properties.ItemAnimationState < 0)
            {
                _nextItemRandomAniTime = 0f;
                return;
            }

            if (_nextItemRandomAniTime <= 0f)
            {
                _nextItemRandomAniTime = Time.time + UnityEngine.Random.Range(_itemRandomAniMinSeconds,
                    _itemRandomAniMaxSeconds);
                return;
            }
            if (Time.time < _nextItemRandomAniTime)
                return;
            _nextItemRandomAniTime = 0f;

            var scheme = _vmCacheService.TryGetItemData(Properties.CurrentItem)?.SchemeName;
            if (string.IsNullOrEmpty(scheme))
                return;

            var candidates = new List<string>();
            for (var i = 1; i <= _maxItemRandomAnis; i++)
            {
                var name = $"T_{scheme}_RANDOM_{i}";
                if (IsPlaying(name))
                    return; // still taking the last drag
                if (_animationService.GetTrack(name, Properties.MdsNameBase, Properties.MdsNameOverlay) != null)
                    candidates.Add(name);
            }
            if (candidates.Count > 0)
                PlayAnimation(candidates[UnityEngine.Random.Range(0, candidates.Count)]);
        }

        /// <summary>
        /// DeveloperConfig.EnableAnimationPfx: *eventPFX (frame, index, name, bone, ATTACH) creates the effect at the
        /// bone (following it if attached), *eventPFXStop (frame, index) lets it fade out. Joint smoke, bubbles, ...
        /// </summary>
        private void ApplyPfxEvents(AnimationTrackInstance trackInstance)
        {
            var stops = trackInstance.GetPendingParticleEffectStops();
            if (stops != null)
            {
                foreach (var stop in stops)
                    StopPfx(stop.Index);
            }

            var pfxEvents = trackInstance.GetPendingParticleEffects();
            if (pfxEvents == null || !_configService.Dev.EnableAnimationPfx)
                return;

            foreach (var pfx in pfxEvents)
            {
                var bone = FindBone(pfx.Position) ?? Go.transform;
                var pfxGo = pfx.Attached
                    ? _meshService.CreateVobPfx(pfx.Name, parent: bone.gameObject, destroyAfterPlay: true)
                    : _meshService.CreateVobPfx(pfx.Name, bone.position, bone.rotation, destroyAfterPlay: true);
                if (pfxGo == null)
                {
                    Logger.LogWarning($"[AnimationPfx] {pfx.Name} couldn't be created ({trackInstance.AnimationName}).",
                        LogCat.Animation);
                    continue;
                }

                var parent = pfxGo.transform.parent;
                var root = parent != null && parent != bone ? parent.gameObject : pfxGo;
                foreach (var particleSystem in root.GetComponentsInChildren<ParticleSystem>())
                {
                    var main = particleSystem.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                }
                if (pfx.Index != 0)
                {
                    StopPfx(pfx.Index);
                    _activePfx[pfx.Index] = root;
                }
                Destroy(root, _pfxMaxSeconds);
            }
        }

        private void StopPfx(int index)
        {
            if (!_activePfx.Remove(index, out var pfxGo) || pfxGo == null)
                return;

            foreach (var particleSystem in pfxGo.GetComponentsInChildren<ParticleSystem>())
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(pfxGo, _pfxFadeSeconds);
        }

        private void StopAllPfx()
        {
            foreach (var index in _activePfx.Keys.ToList())
                StopPfx(index);
        }

        /// <summary>
        /// MDS bone names differ in case from the bone GameObjects ("Bip01 Head" vs "BIP01 HEAD").
        /// </summary>
        private Transform FindBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
                return null;
            foreach (var child in Go.GetComponentsInChildren<Transform>())
            {
                if (child.name.EqualsIgnoreCase(boneName))
                    return child;
            }
            return null;
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

        /// True when the current attack animation has advanced past the DEF_WINDOW start frame.
        /// DEF_WINDOW fires at frame 0 in MDS (stores window bounds as params), so we check elapsed time
        /// against the actual window start frame — matching how VrWeaponAttackDomain reads it.
        public bool HasComboWindowOpened
        {
            get
            {
                if (AttackWindowFrames == null || AttackWindowFrames.Count == 0 || AttackAnimation == null)
                    return false;

                foreach (var trackInstance in _trackInstances)
                {
                    if (!trackInstance.AnimationName.EqualsIgnoreCase(AttackAnimation))
                        continue;
                    var comboStartTime = AttackWindowFrames[0] / trackInstance.Track.FpsSource;
                    return trackInstance.CurrentTime >= comboStartTime;
                }

                return false;
            }
        }

        /// <summary>
        /// True when the attack animation passed its DEF_OPT_FRAME - the frame the engine applies the hit's damage.
        /// Monster attacks (wolves) have no DEF_WINDOW, so HasComboWindowOpened never opened for them.
        /// </summary>
        public bool HasOptimalFrameReached(string animationName)
        {
            if (AttackOptFrame == null || AttackOptFrame.Count == 0)
                return false;

            foreach (var trackInstance in _trackInstances)
            {
                if (!trackInstance.Track.MatchesName(animationName) &&
                    (AttackAnimation == null || !trackInstance.AnimationName.EqualsIgnoreCase(AttackAnimation)))
                    continue;
                return trackInstance.CurrentTime >= AttackOptFrame[0] / trackInstance.Track.FpsSource;
            }

            return false;
        }

        public bool IsPlaying(string animationName)
        {
            foreach (var trackInstance in _trackInstances)
            {
                // Aliases (e.g. t_Bench_S1_2_S0 = t_Bench_S0_2_S1 reversed) are requested by their alias name - comparing
                // the real name only made UseMob treat stand-up transitions as finished at once (pop to standing).
                if (trackInstance.Track.MatchesName(animationName))
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
