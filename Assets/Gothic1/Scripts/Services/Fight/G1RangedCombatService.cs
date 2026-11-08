using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.G1.Services.Fight
{
    /// <summary>
    /// G1 (OpenGothic DamageCalculator::rangeDamageValue): weapon damage only, with a critical hit (talent value as
    /// chance) multiplying it; the hit chance is the shooter's dexterity %.
    /// FIXME - G1 crit (TALENT_BOW/CROSSBOW value % chance) together with the melee talent formula.
    /// </summary>
    public class G1RangedCombatService : IContextRangedCombatService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcHelperService _npcHelperService;


        public float HitChanceReferenceRange => 20f;

        public int GetRangedDamage(NpcContainer shooter, ItemInstance weapon)
        {
            var damage = weapon.DamageTotal;
            var vm = _gameStateService.GothicVm;
            var isCrossbow = weapon.IsCrossbow();
            var talentId = vm.GetSymbolByName(isCrossbow ? "NPC_TALENT_CROSSBOW" : "NPC_TALENT_BOW")?.GetInt(0) ??
                           (isCrossbow ? 4 : 3);
            var chance = _npcHelperService.ExtNpcGetTalentValue(shooter.Instance, talentId);
            var roll = Random.Range(0, 100);
            if (chance <= roll)
                return damage;

            var multiplier = vm.GetSymbolByName(DaedalusConst.DamCriticalMultiplier)?.GetInt(0) ?? 2;
            Logger.Log($"[FightService.RangedHit] {shooter.Instance.GetName(NpcNameSlot.Slot0)}: CRITICAL " +
                       $"({chance} % > roll {roll}) - {damage} x {multiplier}", LogCat.Fight);
            return damage * multiplier;
        }

        public float GetHitChance(NpcContainer shooter, ItemInstance weapon)
        {
            return shooter.Vob.GetAttribute((int)NpcAttribute.Dexterity) / 100f;
        }
    }
}
