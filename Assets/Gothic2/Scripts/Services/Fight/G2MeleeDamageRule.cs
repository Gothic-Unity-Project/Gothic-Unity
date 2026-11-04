using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Context;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.G2.Services.Fight
{
    /// <summary>
    /// G2 (OpenGothic DamageCalculator::swordDamage): the roll is the hit chance (C_NPC.hitchance[1H/2H]) - a miss is
    /// a near miss doing a tenth. Monsters without a weapon always hit.
    /// </summary>
    public class G2MeleeDamageRule : IMeleeDamageRule
    {
        public bool RollHit(NpcContainer attacker, ItemInstance weapon, bool isTwoHanded, int roll)
        {
            return (!attacker.IsHuman() && weapon == null) ||
                   attacker.Instance.GetHitChance(isTwoHanded ? NpcTalent.TwoHanded : NpcTalent.OneHanded) > roll;
        }

        public int TypeDamage(int strength, int typeDamage, int protection, bool isHit)
        {
            var value = Mathf.Max(strength + typeDamage - protection, 0);
            return isHit ? value : (value - 1) / 10;
        }

        public string GetRollName(bool isHit)
        {
            return isHit ? "hit" : "near miss";
        }
    }
}
