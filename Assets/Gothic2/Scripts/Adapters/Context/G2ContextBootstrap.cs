using Gothic.Core.Adapters.Context;
using Gothic.Core.Models.Context;
using Gothic.Core.Services.Context;
using Gothic.G2.Services.Context;
using Gothic.Core;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using ZenKit;

namespace Gothic.G2.Adapters.Context
{
    /// <summary>
    /// Bootstrap class which will register listener to set this module as Active if GameSettings.Controls match.
    /// </summary>
    public class G2ContextBootstrap : AbstractContextBootstrap
    {
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly ContextMeleeDamageService _contextMeleeDamageService;


        protected override void RegisterControlModule(Controls controls)
        {
            // NOP
        }

        protected override void RegisterGameVersionModule(GameVersion version)
        {
            if (version != GameVersion.Gothic2)
                return;

            _contextGameVersionService.SetImpl(new G2ContextService().Inject());
            _contextMeleeDamageService.SetImpl(new G2MeleeDamageService().Inject());
        }
    }
}
