using Gothic.Core.Models.Container;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class GoToNextFp : GoToFp
    {
        public GoToNextFp(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }
    }
}
