using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core;
using Reflex.Attributes;
using ZenKit;

namespace Gothic.G2.Services.Context
{
    public class G2ContextService : IContextGameVersionService
    {
        [Inject] private readonly ConfigService _configService;


        public GameVersion Version => GameVersion.Gothic2;
        string IContextGameVersionService.RootPath => _configService.Root.Gothic2Path;
        public string CutsceneFileSuffix => "LSC";

        // FIXME - Load from GothicGame.ini
        public string InitialWorld => "newworld.zen";
    }
}
