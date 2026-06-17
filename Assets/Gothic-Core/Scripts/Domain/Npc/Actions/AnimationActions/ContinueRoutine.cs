using Gothic.Core.Adapters.Npc;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class ContinueRoutine : AbstractAnimationAction
    {
        public ContinueRoutine(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var ai = PrefabProps.AiHandler ?? NpcGo.GetComponent<AiHandler>();

            if (ai == null)
            {
                Logger.LogWarning($"[ContinueRoutine] AiHandler null on {NpcGo.name} — skipping routine restart", LogCat.Ai);
                IsFinishedFlag = true;
                return;
            }

            ai.ClearState(false);

            var routine = Props.RoutineCurrent;

            // FIXME - Please align logic with StartState.cs handling. (i.e. no call of StartRoutine() directly. Instead handling via ClearState() above.
            ai.StartRoutine(routine.Action, routine.Waypoint);

            IsFinishedFlag = true;
        }
    }
}
