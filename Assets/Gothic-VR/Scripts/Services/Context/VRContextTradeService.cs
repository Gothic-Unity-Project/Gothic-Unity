using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Player;
using Gothic.VR.Adapters.Trade;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services.Context
{
    /// <summary>
    /// Owns the VR trade counter (VRTradeCounter) between the hero and the trader: one while trading, destroyed
    /// afterwards. The counter shows TradeService.Current.
    /// </summary>
    public class VRContextTradeService : IContextTradeService
    {
        [Inject] private readonly TradeService _tradeService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;

        private VRTradeCounter _counter;

        public bool ShowTrade(NpcContainer trader, NpcContainer hero)
        {
            HideTrade();

            var rig = _contextInteractionService.GetCurrentPlayerController()?.GetComponent<IVrRig>();
            if (rig == null || _tradeService.Current == null)
            {
                Logger.LogWarning($"[VRContextTradeService] No VR rig (found: {rig != null}) or no trade session - " +
                                  "no counter", LogCat.VR);
                return false;
            }

            _counter = VRTradeCounter.Spawn(_tradeService.Current, rig.Head);
            return true;
        }

        public void HideTrade()
        {
            if (_counter != null)
                Object.Destroy(_counter.gameObject);
            _counter = null;
        }
    }
}
