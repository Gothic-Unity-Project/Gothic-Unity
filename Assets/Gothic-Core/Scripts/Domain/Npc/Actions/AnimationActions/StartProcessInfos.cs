using System.Linq;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Reflex.Attributes;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
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

            // The chosen choice already left the list before its function ran (DialogService.CallInformation).
            _dialogService.StartDialog(NpcContainer, false);
        }
    }
}
