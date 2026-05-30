using ZenKit;

namespace GUZ.Core.Services.Context
{
    public interface IContextGameVersionService
    {
        GameVersion Version { get; }
        string RootPath { get; }
        string CutsceneFileSuffix { get; }

        /// <summary>
        /// Start world from GothicGame.ini
        /// </summary>
        string InitialWorld { get;  }
    }
}
