using GUZ.Core;
using GUZ.Core.Adapters.Context;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Context;
using GUZ.Core.Services.Context;
using Reflex.Attributes;
using ZenKit;

namespace GUZ.G1.Adapters.Context
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
