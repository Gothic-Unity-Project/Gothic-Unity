using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Player;
using Gothic.VR.Adapters.UI;
using Reflex.Attributes;

namespace Gothic.VR.Services.Context
{
    public class VRContextTradeService : IContextTradeService
    {
        [Inject] private readonly TradeService _tradeService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        public bool ShowTrade(NpcContainer trader, NpcContainer hero)
        {
            VRTradeWindow.Show(trader, hero, _tradeService, _vmCacheService, _vrPlayerService);
            return true;
        }

        public void HideTrade()
        {
            VRTradeWindow.Hide();
        }
    }
}
