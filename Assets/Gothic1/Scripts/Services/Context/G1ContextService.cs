using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core;
using Reflex.Attributes;
using ZenKit;

namespace Gothic.G1
{
    public class G1ContextService : IContextGameVersionService
    {
        [Inject] private readonly ConfigService _configService;


        public GameVersion Version => GameVersion.Gothic1;
        string IContextGameVersionService.RootPath => _configService.EffectiveModPath ?? _configService.Root.Gothic1Path;
        public string CutsceneFileSuffix => "CSL";

        // FIXME - Load from GothicGame.ini
        public string InitialWorld => "world.zen";
    }
}
