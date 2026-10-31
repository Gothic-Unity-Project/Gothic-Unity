using Gothic.Core.Models.Container;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_OutputSVM_Overlay (B_Say_Overlay): a short remark ("Verstehe", "Ooh") while the NPC goes on with what it does,
    /// like the engine's overlay output - no gesture, the queue doesn't wait for the sound.
    /// </summary>
    public class OutputSvmOverlay : OutputSvm
    {
        protected override bool IsOverlay => true;

        public OutputSvmOverlay(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }
    }
}
