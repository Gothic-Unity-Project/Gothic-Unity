using Gothic.Core.Extensions;
using Gothic.Core.Services.Context;
using Gothic.VR.Services.Context;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Gothic's parry in VR (option heroBlock): forwards the frame to VRContextHeroBlockService, whose
    /// VrHeroBlockDomain holds the rule (both hands on the same melee weapon, both triggers held).
    /// </summary>
    public class VRHeroBlock : MonoBehaviour
    {
        [Inject] private readonly ContextHeroBlockService _contextHeroBlockService;

        private VRContextHeroBlockService _heroBlockService;

        private void Awake()
        {
            this.Inject();
            _heroBlockService = _contextHeroBlockService.GetImpl<VRContextHeroBlockService>();
        }

        private void Update()
        {
            _heroBlockService.Tick();
        }
    }
}
