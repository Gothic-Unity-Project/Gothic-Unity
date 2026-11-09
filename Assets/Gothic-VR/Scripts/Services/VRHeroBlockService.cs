using Gothic.Core.Extensions;
using Gothic.VR.Domain.Player;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Gothic's parry in VR (option heroBlock): owns VrHeroBlockDomain, which sets the hero's block props and reports
    /// the NPC weapons touching the held one. Ticked by VRHeroBlock. Whether a hit is blocked is
    /// VRContextHeroBlockService's rule, which reads those props.
    /// </summary>
    public class VRHeroBlockService
    {
        private VrHeroBlockDomain _blockDomain;

        /// <summary>
        /// Called once the VR scope is built (ReflexVRSceneInstaller). A second call keeps the first domain - its
        /// listeners aren't added twice.
        /// </summary>
        public void Init()
        {
            _blockDomain ??= new VrHeroBlockDomain().Inject();
        }

        /// <summary>
        /// Every frame (VRHeroBlock): the hero's block props and the NPC weapons touching the held one.
        /// </summary>
        public void Tick()
        {
            _blockDomain?.Tick();
        }
    }
}
