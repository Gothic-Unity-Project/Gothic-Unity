using System;
using Gothic.Core.Adapters.Context;
using Gothic.Core.Models.Context;
using Gothic.Core;
using Gothic.Core.Services.Context;
using Gothic.Flat.Services.Context;
using Reflex.Attributes;
using ZenKit;

namespace Gothic.Flat.Adapters.Context
{
    /// <summary>
    /// Bootstrap class which will register listener to set this module as Active if GameSettings.Controls match.
    /// </summary>
    public class FlatContextBootstrap : AbstractContextBootstrap
    {
        [Inject] private readonly ContextTextInputService _contextTextInputService;
        [Inject] private readonly ContextScreenshotService _contextScreenshotService;

        protected override void RegisterControlModule(Controls controls)
        {
            if (controls != Controls.Flat)
                return;

            _contextTextInputService.SetImpl(new FlatContextTextInputService());
            _contextScreenshotService.SetImpl(new FlatContextScreenshotService());

            throw new NotImplementedException("FlatContext needs rework.");
        }

        protected override void RegisterGameVersionModule(GameVersion version)
        {
            // NOP
        }
    }
}
