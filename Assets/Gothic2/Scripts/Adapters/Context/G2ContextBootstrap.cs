using GUZ.Core;
using GUZ.Core.Adapters.Context;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Context;
using GUZ.Core.Services.Context;
using GUZ.G2.Services.Context;
using Reflex.Attributes;
using ZenKit;

namespace GUZ.G2.Adapters.Context
{
    /// <summary>
    /// Bootstrap class which will register listener to set this module as Active if GameSettings.Controls match.
    /// </summary>
    public class G2ContextBootstrap : AbstractContextBootstrap
    {
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;


        protected override void RegisterControlModule(Controls controls)
        {
            // NOP
        }

        protected override void RegisterGameVersionModule(GameVersion version)
        {
            if (version != GameVersion.Gothic2)
                return;

            _contextGameVersionService.SetImpl(new G2ContextService().Inject());
        }
    }
}
