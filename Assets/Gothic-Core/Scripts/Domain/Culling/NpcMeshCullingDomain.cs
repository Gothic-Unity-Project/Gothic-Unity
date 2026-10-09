using System;
using System.Collections.Generic;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Creator;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Culling
{
    public class NpcMeshCullingDomain : AbstractCullingDomain
    {
        [Inject] private readonly WayNetService _wayNetService;


        // Real-world sized sphere around an NPC. The culling distance itself is handled via BoundingDistances.
        private const float _npcSphereRadius = 1f;
        private const int _initialCapacity = 256;

        // Shared by reference with the CullingGroup. Grows by doubling. The used entry amount is communicated
        // via SetBoundingSphereCount() - i.e. add/remove won't allocate a new array each time.
        private BoundingSphere[] _spheres = new BoundingSphere[_initialCapacity];
        private NpcLoader[] _loaders = new NpcLoader[_initialCapacity];
        private ObjectState[] _states = new ObjectState[_initialCapacity];
        private int _count;

        private readonly Dictionary<NpcInstance, int> _indexByInstance = new();

        // Indices of NPCs currently in visible range. The loader itself is resolved via _loaders[index],
        // so a set of indices is the single source of truth (no parallel index->loader map to keep in sync).
        private readonly HashSet<int> _visibleNpcs = new();


        public override void PreWorldCreate()
        {
            base.PreWorldCreate();

            _spheres = new BoundingSphere[_initialCapacity];
            _loaders = new NpcLoader[_initialCapacity];
            _states = new ObjectState[_initialCapacity];
            _count = 0;
            _indexByInstance.Clear();
            _visibleNpcs.Clear();
        }

        public void AddCullingEntry(GameObject go)
        {
            var loader = go.GetComponent<NpcLoader>();
            if (loader == null)
            {
                Logger.LogError($"Can't add >{go.name}< to NPC culling as it has no NpcLoader component.", LogCat.Npc);
                return;
            }

            if (_count == _spheres.Length)
            {
                Array.Resize(ref _spheres, _count * 2);
                Array.Resize(ref _loaders, _count * 2);
                Array.Resize(ref _states, _count * 2);

                if (CurrentState == State.WorldLoaded)
                    CullingGroup.SetBoundingSpheres(_spheres);
            }

            var index = _count++;
            _spheres[index] = new BoundingSphere(go.transform.position, _npcSphereRadius);
            _loaders[index] = loader;
            _states[index] = ObjectState.Unknown;
            _indexByInstance[loader.Npc] = index;

            // NPCs spawned at runtime (e.g. summoned monsters) are added after the world finished loading.
            if (CurrentState == State.WorldLoaded)
            {
                CullingGroup.SetBoundingSphereCount(_count);
                ApplyStateByDistance(index);
            }
        }

        /// <summary>
        /// Set main camera once world is loaded fully. Doesn't work at loading time as we change scenes etc.
        /// </summary>
        public override void PostWorldCreate()
        {
            base.PostWorldCreate();

            if (ConfigService.Dev.EnableNpcMeshCulling)
            {
                var cullingDistance = ConfigService.Dev.NpcCullingDistance;

                CullingGroup.SetBoundingSpheres(_spheres);
                CullingGroup.SetBoundingSphereCount(_count);
                CullingGroup.SetBoundingDistances(new[] { cullingDistance, cullingDistance * HysteresisFactor });
                CullingGroup.onStateChanged = VisibilityChanged;
            }

            // Apply the initial state for all NPCs ourselves, as CullingGroup events aren't reliable for
            // spheres which start inside the first distance band. Also handles disabled NPC culling (all visible).
            for (var i = 0; i < _count; i++)
            {
                ApplyStateByDistance(i);
            }
        }

        /// <summary>
        /// Band 0 - [0...cullingDistance)         - NPC is enabled.
        /// Band 1 - [cullingDistance...*1.15)     - Hysteresis zone: NPC keeps its current state to avoid border flickering.
        /// Band 2 - [cullingDistance*1.15...inf)  - NPC is disabled.
        /// </summary>
        protected override void VisibilityChanged(CullingGroupEvent evt)
        {
            if (evt.currentDistance == 0)
                SetNpcState(evt.index, true);
            else if (evt.currentDistance >= 2)
                SetNpcState(evt.index, false);
        }

        private void ApplyStateByDistance(int index)
        {
            if (!ConfigService.Dev.EnableNpcMeshCulling)
            {
                SetNpcState(index, true);
                return;
            }

            var distance = Vector3.Distance(ReferencePoint.position, _spheres[index].position) - _spheres[index].radius;
            SetNpcState(index, distance < ConfigService.Dev.NpcCullingDistance);
        }

        private void SetNpcState(int index, bool isInVisibleRange)
        {
            var desiredState = isInVisibleRange ? ObjectState.Enabled : ObjectState.Disabled;
            if (_states[index] == desiredState)
                return;

            _states[index] = desiredState;

            var loader = _loaders[index];
            var npcData = loader.Container;

            if (!isInVisibleRange && loader.IsLoaded)
            {
                npcData.PrefabProps?.AnimationSystem.StopAllAnimations();
            }

            loader.gameObject.SetActive(isInVisibleRange);

            GlobalEventDispatcher.NpcMeshCullingChanged.Invoke(npcData, loader, isInVisibleRange, true);

            // Alter position tracking of NPC
            if (isInVisibleRange)
            {
                _visibleNpcs.Add(index);
            }
            // When an NPC gets invisible, we need to check for their next respawn from their scheduled routine position.
            else
            {
                npcData.PrefabProps?.AiHandler?.DisableNpc();
                MoveToRoutineWayPoint(index, npcData);
                _visibleNpcs.Remove(index);
            }
        }

        /// <summary>
        /// Called when the time-based routine of an NPC changed. Culled NPCs move their culling sphere
        /// (and lazy-loading GO) to the new scheduled waypoint, so that the world progresses while not looking.
        /// Visible NPCs walk to their new routine spot on their own.
        /// </summary>
        public void OnNpcRoutineChanged(NpcContainer npcData)
        {
            if (!_indexByInstance.TryGetValue(npcData.Instance, out var index))
                return;

            if (_states[index] == ObjectState.Enabled)
                return;

            MoveToRoutineWayPoint(index, npcData);
        }

        /// <summary>
        /// While culled, the next visibility check needs to happen at the position where the routine schedule
        /// expects the NPC - not where it was culled out.
        /// </summary>
        private void MoveToRoutineWayPoint(int index, NpcContainer npcData)
        {
            var props = npcData.Props;

            // Corpses stay where they are.
            if (props == null || props.BodyState == VmGothicEnums.BodyState.BsDead)
                return;

            if (props.RoutineCurrent == null)
                return;

            var wayNetPoint = _wayNetService.GetWayNetPoint(props.RoutineCurrent.Waypoint);
            if (wayNetPoint == null)
                return;

            _spheres[index].position = wayNetPoint.Position;

            // InitNpc() of a not-yet-loaded NPC spawns at this GO's position, so keep it in sync with the sphere.
            // An already-loaded NPC must NOT have its live transform moved here: it would teleport the visible
            // mesh to the routine waypoint mid-walk. Loaded NPCs are repositioned on re-enable via ReEnableNpc().
            if (!_loaders[index].IsLoaded)
                _loaders[index].transform.position = wayNetPoint.Position;
        }

        /// <summary>
        /// Each frame, we update the visible NPCs' current position.
        /// </summary>
        public void Update()
        {
            foreach (var index in _visibleNpcs)
            {
                var rootTransform = _loaders[index].transform;

                // NpcLoader.NPCRoot is only updated after a walking animation's loop is done.
                // child[0] == NPCRoot/BIP01 -> We need to fetch this one as it's the walking animation root which updates every frame.
                if (rootTransform.childCount > 0)
                {
                    var child = rootTransform.GetChild(0);
                    // It might be, that the NPC is not yet initialized. Therefore wait until the GO structure is fully loaded.
                    if (child.childCount > 0)
                    {
                        _spheres[index].position = child.GetChild(0).position;
                    }
                }
            }
        }

        public void UpdateVobPositionOfVisibleNpcs()
        {
            foreach (var index in _visibleNpcs)
            {
                var container = _loaders[index].Container;

                container.Vob.Position = container.PrefabProps.Bip01.position.ToZkVector();
                container.Vob.Rotation = container.PrefabProps.Bip01.rotation.ToZkMatrix();
            }
        }

        public List<NpcContainer> GetVisibleNpcs()
        {
            var visibleNpcs = new List<NpcContainer>(_visibleNpcs.Count);
            foreach (var index in _visibleNpcs)
            {
                visibleNpcs.Add(_loaders[index].Container);
            }

            return visibleNpcs;
        }

        public IEnumerable<NpcContainer> GetAllNpcContainers()
        {
            for (var i = 0; i < _count; i++)
                yield return _loaders[i].Container;
        }
    }
}
