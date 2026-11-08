using Gothic.Core.Models.Container;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Gives an NPC's fire effect. Implemented in Adapters/, registered in ReflexProjectInstaller.
    /// </summary>
    public interface INpcBurnEffectFactory
    {
        /// <summary>
        /// The NPC's fire effect, created on first use. Null without a GameObject.
        /// </summary>
        INpcBurnEffect Get(NpcContainer npc);
    }
}
