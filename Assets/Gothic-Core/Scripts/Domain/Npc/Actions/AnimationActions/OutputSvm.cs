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

            // ZenKit's SvmInstance only knows G1's C_SVM members - G2/mods have many more (e.g. $MISSINGITEM). The vanilla
            // scripts name every entry SVM_<voice>_<Key> (C_SVM.MissingItem = "SVM_15_MissingItem"), the OU.BIN
            // subtitle uses the same name.
            if (_preparedSvmFileName == null && svm != null && !string.IsNullOrEmpty(Action.String0))
                _preparedSvmFileName = $"SVM_{NpcInstance.Voice}_{Action.String0.TrimStart('$')}";

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
