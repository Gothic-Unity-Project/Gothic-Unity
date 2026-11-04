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

        public bool IsRollSuccess(NpcContainer attacker, ItemInstance weapon, bool isTwoHanded, int diceRoll)
        {
            return _impl.IsRollSuccess(attacker, weapon, isTwoHanded, diceRoll);
        }

        public int TypeDamage(int strength, int typeDamage, int protection, bool isRollSuccess)
        {
            return _impl.TypeDamage(strength, typeDamage, protection, isRollSuccess);
        }

        public string GetRollResultName(bool isRollSuccess)
        {
            return _impl.GetRollResultName(isRollSuccess);
        }
    }
}
