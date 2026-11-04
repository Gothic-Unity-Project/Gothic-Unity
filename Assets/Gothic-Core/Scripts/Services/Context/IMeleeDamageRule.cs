using Gothic.Core.Models.Container;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Context
{
    /// <summary>
    /// The game version's melee damage formula (OpenGothic DamageCalculator::swordDamage). Core throws a dice roll
    /// 0-99 per hit (like the engine's rand(100)), sums the damage types and applies the protection
    /// (MeleeDamageDomain). The version compares the dice roll against its chance:
    /// G1: weapon talent value -> critical hit (damage x DAM_CRITICAL_MULTIPLIER),
    /// G2: hitchance -> hit, otherwise a near miss (a tenth of the damage).
    /// </summary>
    public interface IMeleeDamageRule
    {
        /// <summary>
        /// Is the dice roll below the version's chance? G1: critical hit, G2: hit (otherwise a near miss).
        /// </summary>
        /// <param name="weapon">The attacker's melee weapon - null for fists, claws and bites.</param>
        /// <param name="diceRoll">Random 0-99. The roll succeeds if the version's chance is greater.</param>
        bool IsRollSuccess(NpcContainer attacker, ItemInstance weapon, bool isTwoHanded, int diceRoll);

        /// <summary>
        /// The damage of one damage type against a protection of 0 or more (immune types never get here).
        /// </summary>
        /// <param name="typeDamage">The NPC's and its weapon's damage of this type.</param>
        /// <param name="isRollSuccess">Result of IsRollSuccess(): G1 critical hit, G2 hit.</param>
        int TypeDamage(int strength, int typeDamage, int protection, bool isRollSuccess);

        /// <summary>
        /// The dice roll's result for the log (e.g. "CRITICAL", "near miss").
        /// </summary>
        string GetRollResultName(bool isRollSuccess);
    }
}
