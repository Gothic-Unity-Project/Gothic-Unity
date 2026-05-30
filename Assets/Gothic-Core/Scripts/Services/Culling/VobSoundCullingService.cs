using GUZ.Core.Models.Container;
using GUZ.Core.Domain.Culling;
using GUZ.Core.Extensions;
using UnityEngine;
using ZenKit.Vobs;

namespace GUZ.Core.Services.Culling
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
