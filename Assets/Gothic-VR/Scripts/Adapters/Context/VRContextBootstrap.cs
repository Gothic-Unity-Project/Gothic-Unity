using Gothic.Core.Adapters.Context;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Config;
using Gothic.Core.Models.Context;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.VR.Interaction.Backends.Xri;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services.Context;
using Reflex.Attributes;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;
#if GOTHIC_HVR_INSTALLED
using Gothic.VR.Interaction.Backends.Hvr;
#endif

namespace Gothic.VR.Adapters.Context
{
    /// <summary>
    /// Bootstrap class which will register listener to set this module as Active if GameSettings.Controls match.
    /// Also selects the VR backend (interaction framework) which provides our VR ports.
    /// </summary>
    public class VRContextBootstrap : AbstractContextBootstrap
    {
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly ContextMenuService _contextMenuService;
        [Inject] private readonly ContextDialogService _contextDialogService;
        [Inject] private readonly ContextTextInputService _contextTextInputService;
        [Inject] private readonly ConfigService _configService;

#if GOTHIC_HVR_INSTALLED
        public const bool IsHvrInstalled = true;
#else
        public const bool IsHvrInstalled = false;
#endif

        /// <summary>
        /// Returns the VR backend requested by DeveloperConfig.VrInteractionBackend. Hvr falls back to Xri if HVR
        /// isn't compiled in (no GOTHIC_HVR_INSTALLED). Xri is always available.
        /// </summary>
        public static IVrBackend SelectBackend(DeveloperConfig developerConfig)
        {
            switch (GetEffectiveBackend(developerConfig))
            {
#if GOTHIC_HVR_INSTALLED
                case VrInteractionBackend.Hvr:
                    return new HvrBackend();
#endif
                default:
                    return new XriBackend();
            }
        }

        /// <summary>
        /// The backend which SelectBackend() creates. Without a DeveloperConfig (e.g. not set yet), HVR is used if
        /// installed, otherwise XRI.
        /// </summary>
        public static VrInteractionBackend GetEffectiveBackend(DeveloperConfig developerConfig)
        {
            var requested = developerConfig == null
                ? (IsHvrInstalled ? VrInteractionBackend.Hvr : VrInteractionBackend.Xri)
                : developerConfig.VrInteractionBackend;

            if (requested == VrInteractionBackend.Hvr && !IsHvrInstalled)
                return VrInteractionBackend.Xri;

            return requested;
        }

        protected override void RegisterControlModule(Controls controls)
        {
            if (controls != Controls.VR)
                return;

            var developerConfig = _configService.Dev;
            var backend = SelectBackend(developerConfig);
            if (developerConfig != null && developerConfig.VrInteractionBackend == VrInteractionBackend.Hvr &&
                !IsHvrInstalled)
            {
                Logger.LogWarning("DeveloperConfig.VrInteractionBackend is Hvr, but HVR isn't installed " +
                                  "(no scripting define >GOTHIC_HVR_INSTALLED<). Falling back to XRI.", LogCat.VR);
            }

            Logger.Log($"VR backend: {backend.Name}", LogCat.VR);

            // We need to set our VR service now, as the Player.scene loading time (basically this Awake() call) is
            // the first time, when we can set our VR service. But the [Inject] resolves are done already at frame 0
            // at Bootstrap.scene. Therefore, we need to set the VR service via new() and as proxy implementation.
            _contextInteractionService.SetImpl(new VRContextInteractionService());
            _contextMenuService.SetImpl(new VRContextMenuService().Inject());
            _contextDialogService.SetImpl(new VRContextDialogService().Inject());
            _contextTextInputService.SetImpl(new VRContextTextInputService());
        }

        protected override void RegisterGameVersionModule(GameVersion version)
        {
            // NOP
        }
    }
}
