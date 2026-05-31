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
