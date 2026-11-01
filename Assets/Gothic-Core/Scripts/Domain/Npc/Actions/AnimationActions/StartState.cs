using Gothic.Core.Models.Container;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using ZenKit.Daedalus;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class StartState : AbstractAnimationAction
    {
        private int _action => Action.Int0;
        private bool _callEndFunction => Action.Bool0;
        private string _wayPoint => Action.String0;
        private NpcInstance _other => Action.Instance0;
        private NpcInstance _victim => Action.Instance1;
        /// <summary>
        /// The _End function of the state this one replaces (AI_StartState(..., 1, ...)), 0 for none.
        /// </summary>
        public int EndFunction => Action.Int1;
        
        public StartState(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            // e.g., ZS_AssessWarn is triggering itself and resets the StateTime counter. We need to block this by checking
            // if we're already in this state and leave the StateTime intact.

            var ai = PrefabProps.AiHandler;

            // The old state ends with its own other/victim (G1 ZS_Talk_End: the hero's AIV_INVINCIBLE goes back).
            // Without one, other is the hero like for the state's own functions (AiHandler.SetStateGlobals).
            if (EndFunction != 0)
            {
                var other = Props.StateOther ?? (NpcInstance)GameStateService.GothicVm.GlobalHero;
                VmService.CallAsNpc(EndFunction, NpcInstance, other, Props.StateVictim, LogCat.Ai);
            }

            Vob.NextStateIndex = _action;
            Vob.NextStateName = GameStateService.GothicVm.GetSymbolByIndex(_action)!.Name;
            Vob.NextStateValid = true;
            Vob.NextStateIsRoutine = true; // TODO - We need to check if this is correct or if we need to have it more dynamic.
            
            ai.ClearState(_callEndFunction);

            Props.IsStateTimeActive = true;
            Props.StateTime = 0;
            Props.Perceptions.Clear();

            GameStateService.GothicVm.GlobalOther = _other;
            GameStateService.GothicVm.GlobalVictim = _victim;

            // The state's loop gets them too (AiHandler), like the engine keeps them per NPC.
            Props.StateOther = _other;
            Props.StateVictim = _victim;
        }

        /// <summary>
        /// This one is actually no animation, but we need to call Start() only.
        /// </summary>
        public override bool IsFinished()
        {
            return true;
        }
    }
}
