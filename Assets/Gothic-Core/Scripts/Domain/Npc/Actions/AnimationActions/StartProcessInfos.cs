using System.Linq;
using GUZ.Core.Manager;
using GUZ.Core.Models.Container;
using GUZ.Core.Services;
using Reflex.Attributes;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// Hint: This is no Daedalus external. We execute this element whenever the Dialog UI needs to be opened.
    ///       It's simply an AnimationAction for convenience reasons and to align with AnimationQueue.
    /// </summary>
    public class StartProcessInfos : AbstractAnimationAction
    {
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly GameStateService _gameStateService;

        
        private bool _isDialogStarting => Action.Bool0;
        private int _dialogId => Action.Int0;


        public StartProcessInfos(AnimationAction action, NpcContainer npcData) : base(action, npcData)
        {
        }

        public override void Start()
        {
            // Whatever comes next, this is no animation Action and we go on. ;-)
            IsFinishedFlag = true;


            if (_isDialogStarting)
            {
                _dialogService.StartDialog(NpcContainer, true);

                return;
            }


            var isInSubDialog = _gameStateService.Dialogs.CurrentOptions.Any();

            if (isInSubDialog)
            {
                var foundItem = _gameStateService.Dialogs.CurrentOptions
                        .FirstOrDefault(option => option.Function == _dialogId);

                // If a dialog calls Info_ClearChoices(), then the current sub dialog is already gone.
                if (foundItem != null)
                {
                    _gameStateService.Dialogs.CurrentOptions.Remove(foundItem);
                }
            }

            _dialogService.StartDialog(NpcContainer, false);
        }
    }
}
