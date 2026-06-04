using Gothic.Core.Adapters.Context;
using Gothic.Core.Models.Context;
using Gothic.Core.Services.Context;
using Gothic.Core;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using ZenKit;

namespace Gothic.G1.Adapters.Context
{
    /// <summary>
    /// Bootstrap class which will register listener to set this module as Active if GameSettings.Controls match.
    /// </summary>
    public class G1ContextBootstrap : AbstractContextBootstrap
    {
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        protected override void RegisterControlModule(Controls controls)
        {
            // NOP
        }

        protected override void RegisterGameVersionModule(GameVersion version)
        {
            if (version != GameVersion.Gothic1)
                return;

            _contextGameVersionService.SetImpl(new G1ContextService().Inject());
        }
    }
}
