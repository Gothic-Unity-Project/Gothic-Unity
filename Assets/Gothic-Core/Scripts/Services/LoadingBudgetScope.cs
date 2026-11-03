using System;

namespace Gothic.Core.Services
{
    /// <summary>
    /// Returned by FrameSkipperService.UseLoadingBudget(). Disposing it ends the loading budget again.
    /// </summary>
    public readonly struct LoadingBudgetScope : IDisposable
    {
        private readonly FrameSkipperService _frameSkipperService;

        internal LoadingBudgetScope(FrameSkipperService frameSkipperService)
        {
            _frameSkipperService = frameSkipperService;
        }

        public void Dispose()
        {
            _frameSkipperService?.EndLoadingBudget();
        }
    }
}
