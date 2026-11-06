using System;
using Gothic.Core.Models.Container;

namespace Gothic.Core.Domain.Npc
{
    public static class NpcJumpFallExtension
    {
        /// <summary>
        /// Set by the NpcJumpFall adapter at startup: Domain and Services don't know the component, only its interface.
        /// </summary>
        public static Func<NpcContainer, INpcJumpFall> Factory;

        /// <summary>
        /// The NPC's jump/fall/climb handler, created on first use. Null without a GameObject.
        /// </summary>
        public static INpcJumpFall GetJumpFall(this NpcContainer npc)
        {
            return Factory?.Invoke(npc);
        }
    }
}
