using System;
using System.Collections.Generic;
using GUZ.Core.Logging;
using GUZ.Core.Services.StaticCache;
using GUZ.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Debugging
{
    /// <summary>
    /// Use whatever pre-caching data got saved at StaticCacheManager.SaveDebugCache().
    /// The actual loading of this data is hidden behind an Activate switch to ensure we don't waste resources during normal gameplay.
    /// </summary>
    public class PreCachingDebugData : MonoBehaviour
    {
        public bool Activate;

        
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly StaticCacheService _staticCacheService;
        
        private List<Vector3> _lightPositions = new();

        private async void OnValidate()
        {
            if (!Activate)
            {
                return;
            }

            try
            {
                await _staticCacheService.LoadDebugCache(_saveGameService.CurrentWorldName);
                _lightPositions = _staticCacheService.LoadedDebugData.LightPositions;
            }
            catch (Exception e)
            {
                Logger.LogError(e.ToString(), LogCat.Debug);
            }
        }

        private void OnDrawGizmos()
        {
            foreach (var lightPos in _lightPositions)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(lightPos, 1f);
                Gizmos.color = Color.red;
            }
        }

    }
}
