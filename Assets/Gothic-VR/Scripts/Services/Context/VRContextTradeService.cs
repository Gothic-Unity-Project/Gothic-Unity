using Gothic.Core.Logging;
using Gothic.Core.Models.Caches;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Gothic.VR.Adapters.UI;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services.Context
{
    /// <summary>
    /// Owns the VR trade window (prefab VR/Prefabs/UI/TradeWindow): one instance while trading, destroyed afterwards.
    /// </summary>
    public class VRContextTradeService : IContextTradeService
    {
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        private VRTradeWindow _window;

        public bool ShowTrade(NpcContainer trader, NpcContainer hero)
        {
            HideTrade();

            var go = _resourceCacheService.TryGetPrefabObject(PrefabType.UiTradeWindow);
            _window = go != null ? go.GetComponent<VRTradeWindow>() : null;
            if (_window == null)
            {
                Logger.LogError("[VRContextTradeService] Trade window prefab not found", LogCat.Ui);
                if (go != null)
                    Object.Destroy(go);
                return false;
            }

            _window.Show(trader, hero);
            return true;
        }

        public void HideTrade()
        {
            if (_window != null)
                Object.Destroy(_window.gameObject);
            _window = null;
        }
    }
}
