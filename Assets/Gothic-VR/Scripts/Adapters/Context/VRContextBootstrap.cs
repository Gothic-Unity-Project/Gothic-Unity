using Gothic.Core.Adapters.Context;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Context;
using Gothic.Core.Services.Context;
using Gothic.VR.Services.Context;
using Reflex.Attributes;
using ZenKit;

namespace Gothic.VR.Adapters.Context
{
    /// <summary>
    /// Bootstrap class which will register listener to set this module as Active if GameSettings.Controls match.
    /// </summary>
    public class VRContextBootstrap : AbstractContextBootstrap
    {
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly ContextMenuService _contextMenuService;
        [Inject] private readonly ContextDialogService _contextDialogService;
        [Inject] private readonly ContextTextInputService _contextTextInputService;

        protected override void RegisterControlModule(Controls controls)
        {
            if (controls != Controls.VR)
                return;

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
