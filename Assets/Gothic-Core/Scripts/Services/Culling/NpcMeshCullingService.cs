using System.Collections.Generic;
using Gothic.Core.Domain.Culling;
using Gothic.Core.Models.Container;
using Gothic.Core.Extensions;
using UnityEngine;

namespace Gothic.Core.Services.Culling
{
    public class NpcMeshCullingService : AbstractCullingService
    {
        private NpcMeshCullingDomain _npcDomain => Domain as NpcMeshCullingDomain;


        public NpcMeshCullingService()
        {
            Domain = new NpcMeshCullingDomain().Inject();
        }

        public void AddCullingEntry(GameObject go)
        {
            _npcDomain.AddCullingEntry(go);
        }

        /// <summary>
        /// The time-based routine of an NPC changed. The culling domain moves spheres of culled NPCs to the new
        /// scheduled waypoint, so that off-screen NPCs progress their daily routine.
        /// </summary>
        public void NotifyNpcRoutineChanged(NpcContainer npc)
        {
            _npcDomain.OnNpcRoutineChanged(npc);
        }

        /// <summary>
        /// The NPCs that are awake (in visible range) into result, which the caller keeps (no allocation).
        /// </summary>
        public void CollectAwakeNpcs(List<NpcContainer> result)
        {
            _npcDomain.CollectAwakeNpcs(result);
        }

        public void Update()
        {
            _npcDomain.Update();
        }

        public void UpdateVobPositionOfVisibleNpcs()
        {
            _npcDomain.UpdateVobPositionOfVisibleNpcs();
        }

        public List<NpcContainer> GetVisibleNpcs()
        {
            return _npcDomain.GetVisibleNpcs();
        }
    }
}
