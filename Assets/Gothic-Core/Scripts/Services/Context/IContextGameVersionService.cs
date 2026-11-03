using ZenKit;

namespace Gothic.Core.Services.Context
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

        /// <summary>
        /// Load the compiled model script (.msb) before the text one (.mds). Like the G2 engine, mods ship changed
        /// animations only in the .msb.
        /// </summary>
        bool PrefersCompiledModelScript { get; }
    }
}
