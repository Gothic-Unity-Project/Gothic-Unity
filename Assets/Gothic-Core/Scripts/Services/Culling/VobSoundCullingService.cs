using Gothic.Core.Domain.Culling;
using Gothic.Core.Models.Container;
using Gothic.Core.Extensions;
using UnityEngine;
using ZenKit.Vobs;

namespace Gothic.Core.Services.Culling
{
    public class VobSoundCullingService : AbstractCullingService
    {
        private VobSoundCullingDomain _soundDomain => Domain as VobSoundCullingDomain;

        public VobSoundCullingService()
        {
            Domain = new VobSoundCullingDomain().Inject();
        }
        
        public void AddCullingEntry(VobContainer container)
        {
            _soundDomain.AddCullingEntry(container.Go, container.VobAs<ISound>());
        }
        
        public void AddCullingEntry(GameObject go, ISound vob)
        {
            _soundDomain.AddCullingEntry(go, vob);
        }
    }
}
