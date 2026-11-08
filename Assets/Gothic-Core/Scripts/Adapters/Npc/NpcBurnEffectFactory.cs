using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;

namespace Gothic.Core.Adapters.Npc
{
    public class NpcBurnEffectFactory : INpcBurnEffectFactory
    {
        public INpcBurnEffect Get(NpcContainer npc)
        {
            if (npc?.Go == null)
                return null;

            if (npc.Go.TryGetComponent<NpcBurnEffect>(out var burn))
                return burn;

            // Injected here, not in Awake: a culled (inactive) NPC can be hit by fire too.
            return npc.Go.AddComponent<NpcBurnEffect>().Inject();
        }
    }
}
