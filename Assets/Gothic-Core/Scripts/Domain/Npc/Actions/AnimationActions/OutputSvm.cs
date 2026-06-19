using Gothic.Core.Models.Container;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Caches;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class OutputSvm : Output
    {
        private string _preparedSvmFileName;

        // Overwriting this lookup as it let's us reuse the inherited Output class.
        protected override string OutputName => _preparedSvmFileName;

        public OutputSvm(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var svm = VmCacheService.TryGetSvmData(NpcInstance.Voice);
            _preparedSvmFileName = svm?.GetAudioName(Action.String0);

            if (_preparedSvmFileName == null)
            {
                IsFinishedFlag = true;
                return;
            }

            base.Start();

            // Hero SVM or overlay SVM: audio plays, queue continues immediately.
            // Bool0 = true means AI_OutputSVM_Overlay — fire-and-forget for NPC combat chatter.
            if (Action.Int0 == 0)
                StartHeroFireAndForget();
            else if (Action.Bool0)
            {
                PrefabProps.NpcSubtitles.ScheduleHide(_audioPlaySeconds);
                IsFinishedFlag = true;
            }
        }
    }
}
