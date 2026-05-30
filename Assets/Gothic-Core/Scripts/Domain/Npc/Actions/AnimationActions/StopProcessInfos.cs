using GUZ.Core.Models.Container;
using GUZ.Core.Manager;
using GUZ.Core.Services.World;
using Reflex.Attributes;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// Dialog is done. (e.g. END clicked)
    /// Therefore close dialog menu immediately.
    /// </summary>
    public class StopProcessInfos : AbstractAnimationAction
    {
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly StoryService _storyService;


        public StopProcessInfos(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            _dialogService.StopDialog(NpcContainer);
            _storyService.SwitchChapterIfPending();
            IsFinishedFlag = true;
        }
    }
}
