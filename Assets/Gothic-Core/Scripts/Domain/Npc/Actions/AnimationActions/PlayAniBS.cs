using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class PlayAniBs : PlayAni
    {
        private VmGothicEnums.BodyState _bodyState => (VmGothicEnums.BodyState)Action.Int0;


        public PlayAniBs(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            base.Start();
            Props.BodyState = _bodyState;
        }
    }
}
