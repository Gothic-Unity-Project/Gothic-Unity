#if GUZ_HVR_INSTALLED
using GUZ.Core.Adapters.Context;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Context;
using GUZ.Core.Services.Context;
using GUZ.VR.Services.Context;
using Reflex.Attributes;
using ZenKit;

namespace GUZ.VR.Adapters.Context
{
    /// <summary>
    /// Bootstrap class which will register listener to set this module as Active if GameSettings.Controls match.
    /// </summary>
    public class VRContextBootstrap : AbstractContextBootstrap
    {
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly ContextMenuService _contextMenuService;
        [Inject] private readonly ContextDialogService _contextDialogService;

        protected override void RegisterControlModule(Controls controls)
        {
            if (controls != Controls.VR)
                return;

// We register VR only if we have HVR installed.
#if GUZ_HVR_INSTALLED
            // We need to set our VR service now, as the Player.scene loading time (basically this Awake() call) is the first time,
            // when we can set our VR service. But the [Inject] resolves are done already at frame 0 at Bootstrap.scene.
            // Therefore, we need to set the VR service via new() and as proxy implementation.
            _contextInteractionService.SetImpl(new VRContextInteractionService());
            _contextMenuService.SetImpl(new VRContextMenuService().Inject());
            _contextDialogService.SetImpl(new VRContextDialogService().Inject());
#else
            throw new System.ArgumentException(
                "VR context is set, but compiler directive >GUZ_HVR_INSTALLED< isn't set. Did you set up Hurricane VR properly?");
#endif
        }

        protected override void RegisterGameVersionModule(GameVersion version)
        {
            // NOP
        }
    }
}
#endif
