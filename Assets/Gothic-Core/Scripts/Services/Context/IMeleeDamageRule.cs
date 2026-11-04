using Gothic.Core.Models.Container;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Context
{
    /// <summary>
    /// The game version's melee damage formula (OpenGothic DamageCalculator::swordDamage). Core rolls the dice, sums
    /// the damage types and applies the protection (MeleeDamageDomain). The module decides what the roll means and how
    /// one damage type is computed - G1: critical hit, G2: hit or near miss.
    /// </summary>
    public interface IMeleeDamageRule
    {
        /// <summary>
        /// The roll succeeded: G1 a critical hit, G2 a hit (otherwise a near miss).
        /// </summary>
        /// <param name="weapon">The attacker's melee weapon - null for fists, claws and bites.</param>
        /// <param name="roll">Random 0-99.</param>
        bool RollHit(NpcContainer attacker, ItemInstance weapon, bool isTwoHanded, int roll);

        /// <summary>
        /// The damage of one damage type against a protection of 0 or more (immune types never get here).
        /// </summary>
        /// <param name="typeDamage">The NPC's and its weapon's damage of this type.</param>
        /// <param name="isHit">Result of RollHit().</param>
        int TypeDamage(int strength, int typeDamage, int protection, bool isHit);

        /// <summary>
        /// The roll's result for the log (e.g. "CRITICAL", "near miss").
        /// </summary>
        string GetRollName(bool isHit);
    }
}
