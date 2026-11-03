using Gothic.Core.Services.Context;
using UnityEngine;

namespace Gothic.VR.Services.Context
{
    public class VRContextScreenshotService : IContextScreenshotService
    {
        public Texture2D CaptureScreenshot()
        {
            // BothEyes would put both eyes side by side into one image.
            return ScreenCapture.CaptureScreenshotAsTexture(ScreenCapture.StereoScreenCaptureMode.LeftEye);
        }
    }
}
