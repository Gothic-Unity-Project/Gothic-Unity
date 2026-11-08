using Gothic.Core.Models.Container;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// The fire on a burning NPC (the NpcBurnEffect adapter). NpcBurnService gets it from INpcBurnEffectFactory.
    /// </summary>
    public interface INpcBurnEffect
    {
        /// <summary>
        /// Lights the fire, or keeps it burning longer if it already burns.
        /// </summary>
        void Burn(NpcContainer npc);
    }
}
