using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Reflex.Attributes;

namespace Gothic.Lab.AnimationActionMocks
{
    public class LabUseItemToState : UseItemToState
    {
        [Inject] private readonly GameStateService _gameStateService;
        
        public LabUseItemToState(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            // TODO - I don't know about a good injection method now. We need to fix it later...
            // var item = _gameStateService.GothicVm.GetSymbolByName(Action.String0);
            // Action.Int0 = item!.Index;

            base.Start();
        }
    }
}
