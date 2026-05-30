using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Adapters.UI.LoadingBars;
using GUZ.Core.Extensions;

namespace GUZ.Core.Manager
{
    public class LoadingService
    {
        private AbstractLoadingBarHandler _loadingBarHandler;
        private Dictionary<string, float> _progressByType = new();

        private string _currentType;
        private float _currentProgressPerElement;

        private bool _isInitialized;
        // Recalculate and update loading bar next frame if something changed. Do not recalculate with every Tick() (for sake of performance)
        private bool _isDirty;

        public void Update()
        {
            if (_isInitialized && _isDirty)
            {
                _isDirty = false;
                UpdateLoadingBar();
            }
        }

        /// <summary>
        /// Called whenever a world loading starts (i.e. reset loading state and rearrange some data)
        /// </summary>
        public void InitLoading(AbstractLoadingBarHandler loadingBarHandler)
        {
            _loadingBarHandler = loadingBarHandler;
            _progressByType = loadingBarHandler.GetProgressTypes().ToDictionary(i => i, i => 0f);
            _isInitialized = true;
        }
        
        private float CalculateOverallProgress()
        {
            var totalProgress = 0f;
            var numTypes = _progressByType.Count;

            foreach (var progressPair in _progressByType)
            {
                totalProgress += progressPair.Value / numTypes;
            }

            return totalProgress;
        }

        private void UpdateLoadingBar()
        {
            // Calculate the overall progress based on individual progress values
            var overallProgress = CalculateOverallProgress();

            // Update the loading bar with the overall progress
            _loadingBarHandler.ProgressBarImage.fillAmount = overallProgress;
        }

        /// <summary>
        /// We only load one type of game data at once. We can therefore set it initially and call an AddProgress() without parameters later.
        /// </summary>
        public void SetPhase(string type, int amountOfElements)
        {
            SetPhase(type, 1f / amountOfElements);
        }
        
        private void SetPhase(string type, float progressPerElement)
        {
            _currentType = type;
            _currentProgressPerElement = progressPerElement;
        }

        /// <summary>
        /// Add a single progress entry based on SetProgressStep() data.
        /// </summary>
        public void Tick()
        {
            _progressByType[_currentType] += _currentProgressPerElement;
            _isDirty = true; // Recalculate and render ProgressBar update next frame.
        }

        /// <summary>
        /// Our count-calculations for steps can be wrong (e.g. if execution of AddProgress() is called too few times).
        /// Therefore, we clean it up and sanitize the loading bar a little.
        /// </summary>
        public void FinalizePhase()
        {
            if (!_isInitialized)
                return;
            
            _progressByType[_currentType] = 1f;

            _currentType = null;
            _currentProgressPerElement = 0;
        }

        public void StopLoading()
        {
            if (!_isInitialized)
                return;

            _isDirty = false;
            _isInitialized = false;
            
            _loadingBarHandler = null;
            _progressByType.ClearAndReleaseMemory();
            _currentType = null;
            _currentProgressPerElement = 0f;
        }
    }
}
