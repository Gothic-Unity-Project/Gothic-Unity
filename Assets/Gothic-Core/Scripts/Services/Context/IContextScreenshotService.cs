using UnityEngine;

namespace Gothic.Core.Services.Context
{
    public interface IContextScreenshotService
    {
        /// <summary>
        /// Screenshot of what the player sees (e.g. save game thumbnail) - VR: one eye, Flat: the screen.
        /// Needs to be called after WaitForEndOfFrame. The caller destroys the texture.
        /// </summary>
        Texture2D CaptureScreenshot();
    }
}
