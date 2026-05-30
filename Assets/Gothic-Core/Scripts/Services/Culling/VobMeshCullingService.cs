using GUZ.Core.Models.Container;
using GUZ.Core.Domain.Culling;
using GUZ.Core.Extensions;
using UnityEngine;

namespace GUZ.Core.Services.Culling
{
    public class VobMeshCullingService : AbstractCullingService
    {
        private VobMeshCullingDomain _vobDomain => Domain as VobMeshCullingDomain;

        public VobMeshCullingService()
        {
            Domain = new VobMeshCullingDomain().Inject();
        }

        public void OnDrawGizmos()
        {
            _vobDomain.OnDrawGizmos();
        }

        public void AddCullingEntry(VobContainer container)
        {
            _vobDomain.AddCullingEntry(container);
        }

        public void RemoveCullingEntry(VobContainer vobContainer)
        {
            _vobDomain.RemoveCullingEntry(vobContainer);
        }

        public void StartTrackVobPositionUpdates(GameObject go)
        {
            _vobDomain.StartTrackVobPositionUpdates(go);
        }

        public void StopTrackVobPositionUpdates(GameObject go)
        {
            _vobDomain.StopTrackVobPositionUpdates(go);
        }
    }
}
