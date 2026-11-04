using Gothic.Core.Models.Container;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Context
{
    /// <summary>
    /// The melee damage rule the game version module registered (G1ContextBootstrap / G2ContextBootstrap).
    /// </summary>
    public class ContextMeleeDamageRuleService : IMeleeDamageRule
    {
        private IMeleeDamageRule _impl;

        public void SetImpl(IMeleeDamageRule impl)
        {
            _impl = impl;
        }

        public bool RollHit(NpcContainer attacker, ItemInstance weapon, bool isTwoHanded, int roll)
        {
            return _impl.RollHit(attacker, weapon, isTwoHanded, roll);
        }

        public int TypeDamage(int strength, int typeDamage, int protection, bool isHit)
        {
            return _impl.TypeDamage(strength, typeDamage, protection, isHit);
        }

        public string GetRollName(bool isHit)
        {
            return _impl.GetRollName(isHit);
        }
    }
}
