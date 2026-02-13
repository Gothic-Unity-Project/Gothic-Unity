using GUZ.Core;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Context;
using Reflex.Attributes;
using ZenKit;

namespace GUZ.G2.Services.Context
{
    public class G2ContextService : IContextGameVersionService
    {
        [Inject] private readonly ConfigService _configService;


        public GameVersion Version => GameVersion.Gothic2;
        string IContextGameVersionService.RootPath => _configService.Root.Gothic2Path;
        public string CutsceneFileSuffix => "CSL";

        // FIXME - Load from GothicGame.ini
        public string InitialWorld => "newworld.zen";
    }
}
