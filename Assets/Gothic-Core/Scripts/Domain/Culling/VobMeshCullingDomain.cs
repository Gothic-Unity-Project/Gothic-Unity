using System;
using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Debugging;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Config;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.StaticCache;
using Gothic.Core.Extensions;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using NumericsVector3 = System.Numerics.Vector3;

namespace Gothic.Core.Domain.Culling
{
    public class VobMeshCullingDomain : AbstractCullingDomain
    {
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly StaticCacheService _staticCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;


        private const int _initialBucketCapacity = 1024;

        /// <summary>
        /// One CullingGroup per VOB size class. The sphere array is shared by reference with the CullingGroup
        /// and grows by doubling. The used entry amount is communicated via SetBoundingSphereCount() -
        /// i.e. add/remove won't allocate a new array each time.
        /// </summary>
        private class CullingBucket
        {
            public CullingGroup Group;
            public BoundingSphere[] Spheres = new BoundingSphere[_initialBucketCapacity];
            public GameObject[] Objects = new GameObject[_initialBucketCapacity];
            public ObjectState[] States = new ObjectState[_initialBucketCapacity];
            // Grabbed VOBs are ignored by culling until they're released and at rest again.
            public bool[] Paused = new bool[_initialBucketCapacity];
            public int Count;

            public readonly MeshCullingGroup Config;
            public readonly Color GizmoColor;

            public CullingBucket(MeshCullingGroup config, Color gizmoColor)
            {
                Config = config;
                GizmoColor = gizmoColor;
            }

            public void Grow()
            {
                var newSize = Spheres.Length * 2;
                Array.Resize(ref Spheres, newSize);
                Array.Resize(ref Objects, newSize);
                Array.Resize(ref States, newSize);
                Array.Resize(ref Paused, newSize);
            }

            public void Reset()
            {
                Spheres = new BoundingSphere[_initialBucketCapacity];
                Objects = new GameObject[_initialBucketCapacity];
                States = new ObjectState[_initialBucketCapacity];
                Paused = new bool[_initialBucketCapacity];
                Count = 0;
            }
        }

        // Small / Medium / Large
        private CullingBucket[] _buckets;

        // O(1) lookup for removal, pausing and position updates.
        private readonly Dictionary<GameObject, (CullingBucket Bucket, int Index)> _vobIndices = new();

        // Released VOBs we wait for to come to rest, before we re-enable culling for them.
        private readonly Dictionary<GameObject, Rigidbody> _pausedVobsToReenable = new();
        private readonly Dictionary<GameObject, Coroutine> _pausedVobsToReenableCoroutine = new();
        private readonly List<GameObject> _vobsAtRestScratch = new();


        public override void Init()
        {
            if (!ConfigService.Dev.EnableVOBMeshCulling)
                return;

            base.Init();

            _buckets = new[]
            {
                new CullingBucket(ConfigService.Dev.SmallVOBMeshCullingGroup, new Color(.9f, 0, 0)) { Group = CullingGroup },
                new CullingBucket(ConfigService.Dev.MediumVOBMeshCullingGroup, new Color(.5f, 0, 0)) { Group = new CullingGroup() },
                new CullingBucket(ConfigService.Dev.LargeVOBMeshCullingGroup, new Color(.2f, 0, 0)) { Group = new CullingGroup() }
            };

            _unityMonoService.StartCoroutine(StopVobTrackingBasedOnVelocity());
        }

        /// <summary>
        /// This method will only be called within EditorMode. It's tested to not being executed within Standalone mode.
        /// </summary>
        public void OnDrawGizmos()
        {
            if (!Application.isPlaying || !ConfigService.Dev.ShowVOBMeshCullingGizmos || _buckets == null)
            {
                return;
            }

            foreach (var bucket in _buckets)
            {
                Gizmos.color = bucket.GizmoColor;
                for (var i = 0; i < bucket.Count; i++)
                {
                    if (bucket.Objects[i] != null &&
                        bucket.Objects[i].TryGetComponent(out VobCullingGizmo gizmoComp) && gizmoComp.ActivateGizmo)
                    {
                        Gizmos.DrawWireSphere(bucket.Spheres[i].position, bucket.Spheres[i].radius);
                    }
                }
            }
        }

        public override void PreWorldCreate()
        {
            if (!ConfigService.Dev.EnableVOBMeshCulling)
                return;

            base.PreWorldCreate();

            // The base class recreated its CullingGroup. Realign the small bucket and recreate the other ones.
            _buckets[0].Group = CullingGroup;
            for (var i = 1; i < _buckets.Length; i++)
            {
                _buckets[i].Group.Dispose();
                _buckets[i].Group = new CullingGroup();
            }

            foreach (var bucket in _buckets)
            {
                bucket.Reset();
            }

            _vobIndices.Clear();
            _pausedVobsToReenable.Clear();
            _pausedVobsToReenableCoroutine.Clear();
        }

        /// <summary>
        /// Set main camera once world is loaded fully.
        /// Doesn't work at loading time as we change scenes etc.
        /// </summary>
        public override void PostWorldCreate()
        {
            if (!ConfigService.Dev.EnableVOBMeshCulling)
                return;

            base.PostWorldCreate();

            foreach (var bucket in _buckets)
            {
                bucket.Group.targetCamera = Camera.main;
                bucket.Group.SetDistanceReferencePoint(ReferencePoint);
                bucket.Group.SetBoundingDistances(new[]
                    { bucket.Config.CullingDistance, bucket.Config.CullingDistance * HysteresisFactor });
                bucket.Group.SetBoundingSpheres(bucket.Spheres);
                bucket.Group.SetBoundingSphereCount(bucket.Count);
            }

            // Route every bucket's events to VobChanged with that bucket. The closures are created once here
            // (not per frame), so adding a size class (see the FIXME on GetBucket) needs no extra handler.
            foreach (var bucket in _buckets)
            {
                var captured = bucket;
                captured.Group.onStateChanged = evt => VobChanged(evt, captured);
            }

            // Apply the initial state for all VOBs ourselves, as CullingGroup events aren't reliable for
            // spheres which start inside the first distance band.
            foreach (var bucket in _buckets)
            {
                for (var i = 0; i < bucket.Count; i++)
                {
                    ApplyStateByDistance(bucket, i);
                }
            }
        }

        // All buckets are wired via per-bucket closures in PostWorldCreate; the single-group abstract handler
        // from the base is unused for the multi-bucket VOB mesh case.
        protected override void VisibilityChanged(CullingGroupEvent evt)
        {
        }

        /// <summary>
        /// Band 0 - [0...cullingDistance)         - VOB is enabled.
        /// Band 1 - [cullingDistance...*1.15)     - Hysteresis zone: VOB keeps its current state to avoid border flickering.
        /// Band 2 - [cullingDistance*1.15...inf)  - VOB is disabled.
        ///
        /// We deliberately ignore evt.isVisible: Frustum culling of Renderers is done by Unity/URP itself.
        /// SetActive() based frustum culling would just duplicate it with main thread costs on every head turn.
        /// </summary>
        private void VobChanged(CullingGroupEvent evt, CullingBucket bucket)
        {
            if (bucket.Paused[evt.index])
                return;

            if (evt.currentDistance == 0)
                SetVobState(bucket, evt.index, true);
            else if (evt.currentDistance >= 2)
                SetVobState(bucket, evt.index, false);
        }

        private void ApplyStateByDistance(CullingBucket bucket, int index)
        {
            var sphere = bucket.Spheres[index];
            var distance = Vector3.Distance(ReferencePoint.position, sphere.position) - sphere.radius;

            SetVobState(bucket, index, distance < bucket.Config.CullingDistance);
        }

        private void SetVobState(CullingBucket bucket, int index, bool active)
        {
            var desiredState = active ? ObjectState.Enabled : ObjectState.Disabled;
            if (bucket.States[index] == desiredState)
                return;

            bucket.States[index] = desiredState;

            var go = bucket.Objects[index];
            go.SetActive(active);

            if (active)
            {
                GlobalEventDispatcher.VobMeshCullingChanged.Invoke(go);
            }
        }

        public void AddCullingEntry(VobContainer container)
        {
            var go = container.Go;
            if (go == null)
                return;

            // Without culling, we simply enable (and therefore lazy-initialize) every VOB.
            if (!ConfigService.Dev.EnableVOBMeshCulling)
            {
                GlobalEventDispatcher.VobMeshCullingChanged.Invoke(go);
                return;
            }

            var bounds = GetLocalBounds(container);
            if (!bounds.HasValue)
            {
                // e.g. ITMICELLO which has no mesh and therefore no cached Bounds.
                return;
            }

            var sphere = GetSphere(go, bounds.Value);
            var bucket = GetBucket(sphere.radius * 2);

            if (bucket.Count == bucket.Spheres.Length)
            {
                bucket.Grow();
                if (CurrentState == State.WorldLoaded)
                    bucket.Group.SetBoundingSpheres(bucket.Spheres);
            }

            var index = bucket.Count++;
            bucket.Spheres[index] = sphere;
            bucket.Objects[index] = go;
            bucket.States[index] = ObjectState.Unknown;
            bucket.Paused[index] = false;
            _vobIndices[go] = (bucket, index);

            if (CurrentState == State.WorldLoaded)
            {
                bucket.Group.SetBoundingSphereCount(bucket.Count);
                ApplyStateByDistance(bucket, index);
            }
        }

        public void RemoveCullingEntry(VobContainer container)
        {
            var go = container.Go;
            if (go == null || !ConfigService.Dev.EnableVOBMeshCulling)
                return;

            if (!_vobIndices.Remove(go, out var entry))
                return;

            var (bucket, index) = entry;
            var lastIndex = bucket.Count - 1;

            // Swap-remove: move the last entry into the freed slot to keep the arrays dense and all indices stable.
            if (index != lastIndex)
            {
                bucket.Spheres[index] = bucket.Spheres[lastIndex];
                bucket.Objects[index] = bucket.Objects[lastIndex];
                bucket.States[index] = bucket.States[lastIndex];
                bucket.Paused[index] = bucket.Paused[lastIndex];
                _vobIndices[bucket.Objects[index]] = (bucket, index);
            }

            bucket.Objects[lastIndex] = null;
            bucket.Count--;

            if (CurrentState == State.WorldLoaded)
                bucket.Group.SetBoundingSphereCount(bucket.Count);

            // Drop any pending physics tracking for the removed VOB.
            CancelStopTrackVobPositionUpdates(go);
        }

        // FIXME - Particles (like leaves in the forest) will be handled like big vobs, but could potentially
        //         being handled as small ones as leaves shouldn't be visible from 100 of meters away.
        private CullingBucket GetBucket(float size)
        {
            foreach (var bucket in _buckets)
            {
                if (size <= bucket.Config.MaximumObjectSize)
                    return bucket;
            }

            // Bigger than the large bucket's maximum size? Still large.
            return _buckets[^1];
        }

        private BoundingSphere GetSphere(GameObject go, Bounds localBounds)
        {
            var worldCenter = go.transform.TransformPoint(localBounds.center);

            // The sphere needs to enclose the whole (potentially rotated) bbox - i.e. its half diagonal,
            // not just half of its biggest dimension.
            var scaledExtents = Vector3.Scale(localBounds.extents, go.transform.lossyScale);
            var radius = scaledExtents.magnitude;

            return new BoundingSphere(worldCenter, radius);
        }

        /// <summary>
        /// Fetch Mesh Bounds which are in local space. We will later "move" the bbox to the current world space.
        /// </summary>
        private Bounds? GetLocalBounds(VobContainer container)
        {
            Bounds? totalBounds = null;
            AddLocalBounds(container.Vob, container.Vob.Position, ref totalBounds);

            return totalBounds;
        }

        /// <summary>
        /// VOBs can contain child-VOBs which might be Particles, Lights, etc.
        /// We therefore need to sum up the overall Bounds to ensure Culling kicks in correctly.
        /// Child bounds are offset by their position relative to the root VOB. (Their rotation is ignored,
        /// but the resulting sphere uses the full bbox diagonal and therefore stays conservative enough.)
        /// </summary>
        private void AddLocalBounds(IVirtualObject vob, NumericsVector3 rootPosition, ref Bounds? totalBounds)
        {
            Bounds additionalBounds = default;

            switch (vob.Type)
            {
                case VirtualObjectType.zCVobLight:
                    additionalBounds = GetLocalLightBounds((ILight)vob);
                    break;
                default:
                    switch (vob.Visual?.Type)
                    {
                        // We don't support Decal and Pfx so far.
                        case VisualType.Decal:
                        case VisualType.ParticleEffect:
                            break;
                        default:
                            additionalBounds = GetLocalMeshBounds(vob);
                            break;
                    }

                    break;
            }

            if (additionalBounds != default)
            {
                additionalBounds.center += (vob.Position - rootPosition).ToUnityVector();
                Encapsulate(ref totalBounds, additionalBounds);
            }

            foreach (var childVob in vob.Children)
            {
                AddLocalBounds(childVob, rootPosition, ref totalBounds);
            }

            // Fire VOBs children are inside a .zen file
            if (vob.Type == VirtualObjectType.oCMobFire)
            {
                var fireWorld =
                    _resourceCacheService.TryGetWorld(((IFire)vob).VobTree, _contextGameVersionService.Version, true);

                // e.g. "NC_FIREPLACE_STONE" has no VobTree. But could we potentially render it as mesh?
                if (fireWorld == null)
                {
                    return;
                }

                // VobTree positions are local to the fire VOB already.
                foreach (var childFireVob in fireWorld!.RootObjects)
                {
                    AddLocalBounds(childFireVob, NumericsVector3.Zero, ref totalBounds);
                }
            }
        }

        private static void Encapsulate(ref Bounds? totalBounds, Bounds additionalBounds)
        {
            if (totalBounds.HasValue)
            {
                var bounds = totalBounds.Value;
                bounds.Encapsulate(additionalBounds);
                totalBounds = bounds;
            }
            else
            {
                totalBounds = additionalBounds;
            }
        }

        private Bounds GetLocalLightBounds(ILight light)
        {
            // FIXME - Lights shine for the whole mesh they belong to again. :-/
            return new Bounds(Vector3.zero, Vector3.one * light.Range / 100 * 2);
        }

        private Bounds GetLocalMeshBounds(IVirtualObject vob)
        {
            string meshName;
            switch (vob.Type)
            {
                case VirtualObjectType.oCItem:
                    var item = _vmCacheService.TryGetItemData(vob.Name);
                    meshName = item?.Visual;

                    // e.g. ITMICELLO has no mesh
                    if (meshName == null)
                        return default;

                    break;
                default:
                    meshName = vob.Visual?.Name ?? vob.Name;
                    break;
            }

            if (meshName.IsNullOrEmpty())
            {
                return default;
            }

            if (_staticCacheService.LoadedVobsBounds.TryGetValue(meshName, out var bounds))
            {
                return bounds;
            }
            else
            {
                // We can carefully disable this log as some elements aren't cached.
                // e.g., when there is no texture like for OC_DECORATE_V4.3DS
                Logger.LogError($"Couldn't find mesh bounds information from StaticCache for >{meshName}<.", LogCat.Mesh);
                return default;
            }
        }

        public void StartTrackVobPositionUpdates(GameObject go)
        {
            if (!ConfigService.Dev.EnableVOBMeshCulling)
                return;

            // Meshes are always 1...n levels below initially created VobLoader GO. Therefore, we need to fetch its parent for track updates.
            var rootGo = go.GetComponentInParent<VobLoader>().gameObject;

            CancelStopTrackVobPositionUpdates(rootGo);

            if (!_vobIndices.TryGetValue(rootGo, out var entry))
            {
                Logger.LogError($"Couldn't find object in Culling list {rootGo.name}. Culling updates will break.",
                    LogCat.Vob);
                return;
            }

            entry.Bucket.Paused[entry.Index] = true;
        }

        /// <summary>
        /// If we execute Start() and Stop() during a short time frame, we need to cancel all the "stop" features.
        /// e.g. If we start grabbing it while it's still in release-stop mode, we cancel delay Coroutine and loop itself.
        /// </summary>
        private void CancelStopTrackVobPositionUpdates(GameObject rootGo)
        {
            if (_pausedVobsToReenableCoroutine.Remove(rootGo, out var coroutine))
            {
                _unityMonoService.StopCoroutine(coroutine);
            }

            _pausedVobsToReenable.Remove(rootGo);
        }

        /// <summary>
        /// When we release an item from our hands, we need to wait a few frames before the velocity of the object is != 0.
        /// Therefore, we put the object into the list delayed.
        /// </summary>
        public void StopTrackVobPositionUpdates(GameObject go)
        {
            if (!ConfigService.Dev.EnableVOBMeshCulling)
                return;

            // Meshes are always 1...n levels below initially created VobLoader GO. Therefore, we need to fetch its parent for track updates.
            var rootGo = go.GetComponentInParent<VobLoader>().gameObject;

            if (_pausedVobsToReenableCoroutine.ContainsKey(rootGo))
            {
                return;
            }

            _pausedVobsToReenableCoroutine.Add(rootGo,
                _unityMonoService.StartCoroutine(StopTrackVobPositionUpdatesDelayed(rootGo)));
        }

        /// <summary>
        /// When we release an item from our hands, we need to wait a few frames before the velocity of the object is != 0.
        /// Therefore, we put the object into the list delayed.
        /// </summary>
        private IEnumerator StopTrackVobPositionUpdatesDelayed(GameObject rootGo)
        {
            yield return new WaitForSeconds(1f);
            _pausedVobsToReenableCoroutine.Remove(rootGo);
            _pausedVobsToReenable.TryAdd(rootGo, rootGo.GetComponentInChildren<Rigidbody>());
        }

        /// <summary>
        /// Iterate over all currently non-kinematic (physical) items (e.g. after grab stopped).
        /// We then look if their velocity is zero to:
        /// 1. update culling position once
        /// 2. stop physics again
        /// </summary>
        private IEnumerator StopVobTrackingBasedOnVelocity()
        {
            while (true)
            {
                if (_pausedVobsToReenable.Count != 0)
                {
                    _vobsAtRestScratch.Clear();

                    foreach (var pausedVob in _pausedVobsToReenable)
                    {
                        if (pausedVob.Value.linearVelocity == Vector3.zero)
                        {
                            _vobsAtRestScratch.Add(pausedVob.Key);
                        }
                    }

                    foreach (var go in _vobsAtRestScratch)
                    {
                        _pausedVobsToReenable[go].isKinematic = true;
                        _pausedVobsToReenable.Remove(go);
                        ResumeCullingAtCurrentPosition(go);
                    }
                }

                yield return null;
            }
        }

        private void ResumeCullingAtCurrentPosition(GameObject go)
        {
            if (!_vobIndices.TryGetValue(go, out var entry))
                return;

            entry.Bucket.Spheres[entry.Index].position = go.transform.position;
            entry.Bucket.Paused[entry.Index] = false;
        }

        public override void OnApplicationQuit()
        {
            if (!ConfigService.Dev.EnableVOBMeshCulling)
                return;

            foreach (var bucket in _buckets)
            {
                bucket.Group.Dispose();
            }
        }
    }
}
