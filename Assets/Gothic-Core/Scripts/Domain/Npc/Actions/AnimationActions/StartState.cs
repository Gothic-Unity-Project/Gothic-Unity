using GUZ.Core.Models.Container;
using GUZ.Core.Const;
using GUZ.Core.Services;
using ZenKit.Daedalus;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class StartState : AbstractAnimationAction
    {
        private int _action => Action.Int0;
        private bool _callEndFunction => Action.Bool0;
        private string _wayPoint => Action.String0;
        private NpcInstance _other => Action.Instance0;
        private NpcInstance _victim => Action.Instance1;
        
        public StartState(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            // e.g., ZS_AssessWarn is triggering itself and resets the StateTime counter. We need to block this by checking
            // if we're already in this state and leave the StateTime intact.

            var ai = PrefabProps.AiHandler;

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
