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
            _preparedSvmFileName = svm.GetAudioName(Action.String0);

            base.Start();
        }
    }
}
