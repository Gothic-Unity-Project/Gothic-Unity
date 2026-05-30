using GUZ.Core;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Context;
using Reflex.Attributes;
using ZenKit;

namespace GUZ.G1
{
    public class G1ContextService : IContextGameVersionService
    {
        [Inject] private readonly ConfigService _configService;


        public GameVersion Version => GameVersion.Gothic1;
        string IContextGameVersionService.RootPath => _configService.Root.Gothic1Path;
        public string CutsceneFileSuffix => "CSL";

        // FIXME - Load from GothicGame.ini
        public string InitialWorld => "world.zen";
    }
}
