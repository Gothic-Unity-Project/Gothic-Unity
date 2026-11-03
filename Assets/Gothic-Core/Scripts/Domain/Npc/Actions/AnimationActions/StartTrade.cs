using Gothic.Core.Models.Container;
using Gothic.Core.Services.Player;
using Reflex.Attributes;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// No Daedalus external: a trade dialog entry (C_Info.trade) queues it after its lines, like StartProcessInfos for
    /// other entries. Trading starts, the choices come back once it ends (TradeService).
    /// </summary>
    public class StartTrade : AbstractAnimationAction
    {
        [Inject] private readonly TradeService _tradeService;

        private int _information => Action.Int0;

        public StartTrade(AnimationAction action, NpcContainer npcData) : base(action, npcData)
        {
        }

        public override void Start()
        {
            IsFinishedFlag = true;
            _tradeService.Start(NpcContainer, _information);
        }
    }
}
