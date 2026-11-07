using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using ZenKit.Daedalus;

namespace Gothic.G2.Services.Fight
{
    /// <summary>
    /// G2 (OpenGothic DamageCalculator::rangeDamageValue): dexterity + weapon damage; the hit chance is the NPC's
    /// hit chance in the bow/crossbow talent.
    /// </summary>
    public class G2RangedCombatService : IContextRangedCombatService
    {
        [Inject] private readonly GameStateService _gameStateService;


        public float HitChanceReferenceRange => 15f;

        public int GetRangedDamage(NpcContainer shooter, ItemInstance weapon)
        {
            return weapon.DamageTotal + shooter.Vob.GetAttribute((int)NpcAttribute.Dexterity);
        }

        public float GetHitChance(NpcContainer shooter, ItemInstance weapon)
        {
            var isCrossbow = weapon != null &&
                             ((VmGothicEnums.ItemFlags)weapon.Flags).HasFlag(VmGothicEnums.ItemFlags.ItemCrossbow);
            var talentName = isCrossbow ? "NPC_TALENT_CROSSBOW" : "NPC_TALENT_BOW";
            var talent = _gameStateService.GothicVm.GetSymbolByName(talentName)?.GetInt(0) ?? (isCrossbow ? 4 : 3);
            return shooter.Vob.GetHitChance(talent) / 100f;
        }
    }
}
