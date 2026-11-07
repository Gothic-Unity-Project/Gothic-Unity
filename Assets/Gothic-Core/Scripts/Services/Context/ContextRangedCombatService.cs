using Gothic.Core.Models.Container;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Context
{
    /// <summary>
    /// The ranged combat rules the game version module registered (G1ContextBootstrap / G2ContextBootstrap).
    /// </summary>
    public class ContextRangedCombatService : IContextRangedCombatService
    {
        private IContextRangedCombatService _impl;

        public void SetImpl(IContextRangedCombatService impl)
        {
            _impl = impl;
        }

        public float HitChanceReferenceRange => _impl.HitChanceReferenceRange;

        public int GetRangedDamage(NpcContainer shooter, ItemInstance weapon)
        {
            return _impl.GetRangedDamage(shooter, weapon);
        }

        public float GetHitChance(NpcContainer shooter, ItemInstance weapon)
        {
            return _impl.GetHitChance(shooter, weapon);
        }
    }
}
