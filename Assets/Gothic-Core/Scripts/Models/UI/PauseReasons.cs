using System;

namespace Gothic.Core.Models.UI
{
    /// <summary>
    /// Why the world is paused right now (PauseService). It runs again once every reason resumed.
    /// </summary>
    [Flags]
    public enum PauseReasons
    {
        None = 0,
        Menu = 1 << 0,
        Video = 1 << 1
    }
}
