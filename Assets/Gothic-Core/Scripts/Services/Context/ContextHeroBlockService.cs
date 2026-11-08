using System;
using Gothic.Core.Models.Container;

namespace Gothic.Core.Services.Context
{
    /// <summary>
    /// The hero block the platform module registered (VRContextBootstrap / FlatContextBootstrap).
    /// </summary>
    public class ContextHeroBlockService : IContextHeroBlockService
    {
        private IContextHeroBlockService _impl;

        public void SetImpl(IContextHeroBlockService impl)
        {
            _impl = impl;
        }

        public bool IsBlocking(NpcContainer hero)
        {
            return _impl.IsBlocking(hero);
        }

        public int GetBlockWeaponMaterial(NpcContainer hero)
        {
            return _impl.GetBlockWeaponMaterial(hero);
        }

        public void ReportBlockContact(NpcContainer attacker)
        {
            _impl.ReportBlockContact(attacker);
        }

        public bool TryBlock(NpcContainer attacker, NpcContainer hero, Action onBlocked, Action onMissed)
        {
            return _impl.TryBlock(attacker, hero, onBlocked, onMissed);
        }
    }
}
