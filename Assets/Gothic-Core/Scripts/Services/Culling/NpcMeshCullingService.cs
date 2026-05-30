using System.Collections.Generic;
using GUZ.Core.Models.Container;
using GUZ.Core.Domain.Culling;
using GUZ.Core.Extensions;
using UnityEngine;

namespace GUZ.Core.Services.Culling
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
