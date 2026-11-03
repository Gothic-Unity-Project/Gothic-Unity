using UnityEngine;

namespace Gothic.Core.Services.Context
{
    public class ContextScreenshotService : IContextScreenshotService
    {
        private IContextScreenshotService _impl;

        public void SetImpl(IContextScreenshotService impl)
        {
            _impl = impl;
        }

        public Texture2D CaptureScreenshot()
        {
            return _impl.CaptureScreenshot();
        }
    }
}
