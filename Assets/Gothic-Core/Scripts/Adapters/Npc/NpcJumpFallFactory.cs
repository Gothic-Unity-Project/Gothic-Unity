using Gothic.Core.Domain.Npc;
using Gothic.Core.Models.Container;

namespace Gothic.Core.Adapters.Npc
{
    public class NpcJumpFallFactory : INpcJumpFallFactory
    {
        public INpcJumpFall Get(NpcContainer npc)
        {
            return NpcJumpFall.Get(npc);
        }

        public void Wake(NpcContainer npc)
        {
            NpcJumpFall.Wake(npc?.Go);
        }
    }
}
