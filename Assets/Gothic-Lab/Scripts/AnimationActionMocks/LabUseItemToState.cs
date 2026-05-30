using GUZ.Core.Domain.Npc.Actions;
using GUZ.Core.Domain.Npc.Actions.AnimationActions;
using GUZ.Core.Models.Container;
using GUZ.Core.Services;
using Reflex.Attributes;

namespace GUZ.Lab.AnimationActionMocks
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
