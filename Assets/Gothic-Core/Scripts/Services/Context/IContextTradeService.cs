using Gothic.Core.Models.Container;

namespace Gothic.Core.Services.Context
{
    public interface IContextTradeService
    {
        /// <returns>False if this platform has no trade window - the trade then ends at once.</returns>
        public bool ShowTrade(NpcContainer trader, NpcContainer hero);
        public void HideTrade();
    }
}
