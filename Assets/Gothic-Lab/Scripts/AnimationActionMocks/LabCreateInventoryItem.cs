using GUZ.Core.Domain.Npc.Actions;
using GUZ.Core.Models.Container;
using GUZ.Core.Services;
using GUZ.Core.Services.Npc;
using Reflex.Attributes;

namespace GUZ.Lab.AnimationActionMocks
{
    public class LabCreateInventoryItem : AbstractLabAnimationAction
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;

        public LabCreateInventoryItem(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var itemSymbol = _gameStateService.GothicVm.GetSymbolByName(Action.String0);

            _npcInventoryService.ExtCreateInvItems(NpcInstance, itemSymbol!.Index, 1);

            base.Start();
        }
    }
}
