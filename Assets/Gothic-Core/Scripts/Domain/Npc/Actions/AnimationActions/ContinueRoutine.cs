using GUZ.Core.Models.Container;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class ContinueRoutine : AbstractAnimationAction
    {
        public ContinueRoutine(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var ai = PrefabProps.AiHandler;

            ai.ClearState(false);

            var routine = Props.RoutineCurrent;

            // FIXME - Please align logic with StartState.cs handling. (i.e. no call of StartRoutine() directly. Instead handling via ClearState() above.
            ai.StartRoutine(routine.Action, routine.Waypoint);

            IsFinishedFlag = true;
        }
    }
}
