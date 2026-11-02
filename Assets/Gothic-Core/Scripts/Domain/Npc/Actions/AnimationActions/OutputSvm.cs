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
            // DeveloperConfig.EnableSvmKeyTrim: G1's ZS_ClearRoom says "$WhyAreYouInHere " (trailing space) - not mapped,
            // the NPC moved his lips without sound and subtitle.
            var key = Action.String0;
            if (ConfigService.Dev.EnableSvmKeyTrim && key != null)
                key = key.Trim();
            _preparedSvmFileName = svm?.GetAudioName(key);

            // ZenKit's SvmInstance only knows G1's C_SVM members. Vanilla scripts name every entry SVM_<voice>_<key>,
            // like the OU.BIN subtitle.
            if (_preparedSvmFileName == null && svm != null && !string.IsNullOrEmpty(key))
                _preparedSvmFileName = $"SVM_{NpcInstance.Voice}_{key.TrimStart('$')}";

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
