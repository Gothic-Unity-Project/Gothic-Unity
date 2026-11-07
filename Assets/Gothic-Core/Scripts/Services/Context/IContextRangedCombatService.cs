using Gothic.Core.Models.Container;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Context
{
    /// <summary>
    /// The game version's ranged combat rules (OpenGothic DamageCalculator::rangeDamageValue): how much a bow or
    /// crossbow hit hurts and how likely a shot hits. Core (FightService, ProjectileService) asks, G1/G2 answer.
    /// </summary>
    public interface IContextRangedCombatService
    {
        /// <summary>
        /// Distance (m) at which the hit chance equals GetHitChance(); it is 100% at the shooter.
        /// </summary>
        float HitChanceReferenceRange { get; }

        /// <summary>
        /// Raw damage of a ranged hit before the damage scale and protection. G2: DEX + weapon damage,
        /// G1: weapon damage, multiplied on a critical hit (talent value as chance).
        /// </summary>
        int GetRangedDamage(NpcContainer shooter, ItemInstance weapon);

        /// <summary>
        /// The shooter's chance (0-1) to hit at the reference range. G1: dexterity %, G2: hit chance of the bow or
        /// crossbow talent.
        /// </summary>
        float GetHitChance(NpcContainer shooter, ItemInstance weapon);
    }
}
