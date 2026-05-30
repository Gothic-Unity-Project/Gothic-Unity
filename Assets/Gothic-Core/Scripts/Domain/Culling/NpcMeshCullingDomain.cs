using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Adapters.Npc;
using GUZ.Core.Logging;
using GUZ.Core.Creator;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Container;
using GUZ.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Domain.Culling
{
    public class NpcMeshCullingDomain : AbstractCullingDomain
    {
        [Inject] private readonly WayNetService _wayNetService;


        // Sphere values to track and update when visible NPCs move.
        private BoundingSphere[] _spheres;
        private readonly Dictionary<int, Transform> _visibleNpcs = new();


        public override void PreWorldCreate()
        {
            base.PreWorldCreate();
            _spheres = null;
            _visibleNpcs.ClearAndReleaseMemory();
        }

        public void AddCullingEntry(GameObject go)
        {
            if (CurrentState != State.Loading)
            {
                Logger.LogWarning($"CullingGroup for Sounds closed already. Can't add >{go.name}<", LogCat.Mesh);
                return;
            }

            Objects.Add(go);

            // Normally NPC spheres are ~1 meters in radius. But we need to fake the volume, so that Culling always thinks
            // we're "inside" the NPC and Frustum+Occlusion Culling isn't triggered.
            // (@see VobSoundCullingManager where we also use it exactly that way, and it works.)
            var sphere = new BoundingSphere(go.transform.position, ConfigService.Dev.NpcCullingDistance);
            Spheres.Add(sphere);
        }

        /// <summary>
        /// Set main camera once world is loaded fully. Doesn't work at loading time as we change scenes etc.
        /// </summary>
        public override void PostWorldCreate()
        {
            if (ConfigService.Dev.EnableNpcMeshCulling)
            {
                base.PostWorldCreate();

                // For performance reasons, we initially used a List during creation.
                // Now we move to an array which is copied by reference to CullingGroup and can be updated later via Update().
                _spheres = Spheres.ToArray();
                CullingGroup.SetBoundingSpheres(_spheres);

                // As we "faked" the volume of NPCs, we will plainly disable them whenever we are out of their volume (aka range).
                CullingGroup.SetBoundingDistances(new[] { 0f });

                CullingGroup.onStateChanged = VisibilityChanged;
            }
            // If we disabled NPC culling, then we need to render them all now!
            else
            {
                Objects.ForEach(obj => obj.SetActive(true));
            }

            // Cleanup
            Spheres.ClearAndReleaseMemory();
        }

        protected override void VisibilityChanged(CullingGroupEvent evt)
        {
            var go = Objects[evt.index];

            // A higher distance level means "invisible" as we only leverage: 0 -> in-range; 1 -> out-of-range.
            var isInVisibleRange = evt.currentDistance == 0;
            var wasOutOfDistance = evt.previousDistance != 0;

            var loaderComp = go.GetComponent<NpcLoader>();
            var npcData = loaderComp.Npc.GetUserData();
            var isInitialized = loaderComp.IsLoaded;

            if (!isInVisibleRange && isInitialized)
            {
                npcData.PrefabProps?.AnimationSystem.StopAllAnimations();
            }

            go.SetActive(isInVisibleRange);
            
            GlobalEventDispatcher.NpcMeshCullingChanged.Invoke(npcData, loaderComp, isInVisibleRange, wasOutOfDistance);

            // Alter position tracking of NPC
            if (isInVisibleRange)
            {
                _visibleNpcs.TryAdd(evt.index, go.transform);
            }
            // When an NPC gets invisible, we need to check for their next respawn from their initially spawned position.
            else
            {
                var props = npcData.Props;

                npcData.PrefabProps?.AiHandler?.DisableNpc();

                if (props.RoutineCurrent != null)
                {
                    var spawnedWayPointName = props.RoutineCurrent.Waypoint;
                    var wayNetPoint = _wayNetService.GetWayNetPoint(spawnedWayPointName);

                    if (wayNetPoint is not null)
                    {
                        UpdatePosition(evt.index, wayNetPoint.Position);
                    }
                }
                _visibleNpcs.Remove(evt.index);
            }
        }

        /// <summary>
        /// Each frame, we update the visible NPCs' current position.
        /// </summary>
        public void Update()
        {
            foreach (var npc in _visibleNpcs)
            {
                // NpcLoader.NPCRoot is only updated after a walking animation's loop is done.
                // child[0] == NPCRoot/BIP01 -> We need to fetch this one as it's the walking animation root which updates every frame.
                if (npc.Value.childCount > 0)
                {
                    var child = npc.Value.GetChild(0);
                    // It might be, that the NPC is not yet initialized. Therefore wait until the GO structure is fully loaded.
                    if (child.childCount > 0)
                    {
                        UpdatePosition(npc.Key, child.GetChild(0).position);
                    }
                }
            }
        }

        public void UpdateVobPositionOfVisibleNpcs()
        {
            foreach (var npc in _visibleNpcs.Values)
            {
                var container = npc.GetComponent<NpcLoader>().Container;

                container.Vob.Position = container.PrefabProps.Bip01.position.ToZkVector();
                container.Vob.Rotation = container.PrefabProps.Bip01.rotation.ToZkMatrix();
            }
        }

        public List<NpcContainer> GetVisibleNpcs()
        {
            return _visibleNpcs.Values.Select(i => i.GetComponent<NpcLoader>().Container).ToList();
        }

        private void UpdatePosition(int sphereKey, Vector3 position)
        {
            _spheres[sphereKey].position = position;
        }
    }
}
