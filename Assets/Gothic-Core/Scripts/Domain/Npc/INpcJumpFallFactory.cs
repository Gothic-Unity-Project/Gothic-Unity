using Gothic.Core.Models.Container;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Gives an NPC's jump/fall/climb handler. Implemented in Adapters/, registered in ReflexProjectInstaller.
    /// </summary>
    public interface INpcJumpFallFactory
    {
        /// <summary>
        /// The NPC's handler, created on first use. Null without a GameObject.
        /// </summary>
        INpcJumpFall Get(NpcContainer npc);

        /// <summary>
        /// Something moved the NPC (a hit): its handler, if it has one, checks for a fall again. Never adds one.
        /// </summary>
        void Wake(NpcContainer npc);
    }
}
