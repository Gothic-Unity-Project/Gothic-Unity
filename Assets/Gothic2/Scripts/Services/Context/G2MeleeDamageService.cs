using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Context;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.G2.Services.Context
{
    /// <summary>
    /// G2 melee damage (OpenGothic DamageCalculator::swordDamage): the hit chance (C_NPC.hitchance[1H/2H]) decides
    /// between a hit and a near miss, which does a tenth. Monsters without a weapon always hit.
    /// </summary>
    public class G2MeleeDamageService : IContextMeleeDamageService
    {
        public bool IsFullHit(NpcContainer attacker, ItemInstance weapon, bool isTwoHanded, int roll)
        {
            if (!attacker.IsHuman() && weapon == null)
                return true;

            return attacker.Instance.GetHitChance(isTwoHanded ? NpcTalent.TwoHanded : NpcTalent.OneHanded) > roll;
        }

        public int GetTypeDamage(int strength, int typeDamage, int protection, bool isFullHit)
        {
            var damage = Mathf.Max(strength + typeDamage - protection, 0);
            return isFullHit ? damage : (damage - 1) / 10;
        }
    }
}
