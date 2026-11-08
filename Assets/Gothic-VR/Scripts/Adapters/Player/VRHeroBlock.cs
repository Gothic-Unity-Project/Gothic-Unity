using Gothic.Core.Extensions;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Gothic's parry in VR (option heroBlock): forwards the frame to VRHeroBlockService, whose VrHeroBlockDomain
    /// holds the rule (both hands on the same melee weapon, both triggers held).
    /// </summary>
    public class VRHeroBlock : MonoBehaviour
    {
        [Inject] private readonly VRHeroBlockService _heroBlockService;

        private void Awake()
        {
            // Added by VRContextInteractionService at runtime: no scene injection, the player scope is built already.
            this.Inject();
        }

        private void Update()
        {
            _heroBlockService.Tick();
        }
    }
}
