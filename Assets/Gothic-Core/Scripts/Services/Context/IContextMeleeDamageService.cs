using Gothic.Core.Models.Container;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Context
{
    /// <summary>
    /// The part of the engine's melee damage that differs between the games (OpenGothic DamageCalculator::swordDamage).
    /// Implemented by the Gothic1/Gothic2 modules.
    /// </summary>
    public interface IContextMeleeDamageService
    {
        /// <summary>
        /// The hit counts fully. G1: a critical hit with the weapon talent's chance (weapons only). G2: a hit by the
        /// hit chance, else a near miss; monsters without a weapon always hit. roll: 0-99.
        /// </summary>
        bool IsFullHit(NpcContainer attacker, ItemInstance weapon, bool isTwoHanded, int roll);

        /// <summary>
        /// Damage of one damage type the target isn't immune to.
        /// </summary>
        int GetTypeDamage(int strength, int typeDamage, int protection, bool isFullHit);
    }
}
