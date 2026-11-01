using Gothic.Core.Models.Container;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Context
{
    public class ContextMeleeDamageService : IContextMeleeDamageService
    {
        private IContextMeleeDamageService _impl;

        public void SetImpl(IContextMeleeDamageService proxy)
        {
            _impl = proxy;
        }

        public bool IsFullHit(NpcContainer attacker, ItemInstance weapon, bool isTwoHanded, int roll)
        {
            return _impl.IsFullHit(attacker, weapon, isTwoHanded, roll);
        }

        public int GetTypeDamage(int strength, int typeDamage, int protection, bool isFullHit)
        {
            return _impl.GetTypeDamage(strength, typeDamage, protection, isFullHit);
        }
    }
}
