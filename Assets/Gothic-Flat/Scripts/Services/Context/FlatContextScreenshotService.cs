using Gothic.Core.Services.Context;
using UnityEngine;

namespace Gothic.Flat.Services.Context
{
    public class FlatContextScreenshotService : IContextScreenshotService
    {
        public Texture2D CaptureScreenshot()
        {
            return ScreenCapture.CaptureScreenshotAsTexture();
        }
    }
}
