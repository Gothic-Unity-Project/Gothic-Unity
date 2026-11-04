using Gothic.Core.Models.Container;

namespace Gothic.Core.Services.Context
{
    /// <summary>
    /// Without an implementation (Flat for now) there is no trade window: ShowTrade returns false, so TradeService
    /// ends the trade and the dialog's choices come back instead of waiting for a window that never opens.
    /// </summary>
    public class ContextTradeService : IContextTradeService
    {
        private IContextTradeService _impl;

        public void SetImpl(IContextTradeService impl)
        {
            _impl = impl;
        }

        public bool ShowTrade(NpcContainer trader, NpcContainer hero)
        {
            return _impl != null && _impl.ShowTrade(trader, hero);
        }

        public void HideTrade()
        {
            _impl?.HideTrade();
        }
    }
}
