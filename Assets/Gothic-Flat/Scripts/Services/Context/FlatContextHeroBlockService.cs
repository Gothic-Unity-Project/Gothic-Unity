using System;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Context;

namespace Gothic.Flat.Services.Context
{
    /// <summary>
    /// Flat has no hero block (yet) - every hit lands, like before the VR block existed.
    /// </summary>
    public class FlatContextHeroBlockService : IContextHeroBlockService
    {
        public bool IsBlocking(NpcContainer hero)
        {
            return false;
        }

        public int GetBlockWeaponMaterial(NpcContainer hero)
        {
            return -1;
        }

        public void ReportBlockContact(NpcContainer attacker)
        {
        }

        public bool TryBlock(NpcContainer attacker, NpcContainer hero, Action onBlocked, Action onMissed)
        {
            return false;
        }
    }
}
