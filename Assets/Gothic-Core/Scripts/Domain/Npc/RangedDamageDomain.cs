using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// The engine's rules for arrows and bolts (OpenGothic DamageCalculator::rangeDamage, Npc::shootBow): whether a shot
    /// hits - certain up close, the shooter's chance at the reference range, none beyond 45 m - and what it takes off.
    /// G1 aims with dexterity and may hit critically by its bow/crossbow talent, G2 aims with its hit chance and adds
    /// dexterity to the damage. Owned by FightService.
    /// </summary>
    public class RangedDamageDomain
    {
        // In m: shots hit with the shooter's own chance here, less beyond, never past the maximum.
        private const float _referenceRangeG1 = 20f;
        private const float _referenceRangeG2 = 15f;
        private const float _maxRange = 45f;
        private const int _npcFlagImmortal = 2;
        private const int _damageTypeCount = 8;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        public float MaxRange => _maxRange;

        public float ReferenceRange => _contextGameVersionService.IsGothic2() ? _referenceRangeG2 : _referenceRangeG1;

        /// <param name="distance">The arrow's flight in m.</param>
        public bool RollHit(NpcContainer shooter, ItemInstance weapon, float distance)
        {
            var chance = GetHitChance(shooter, weapon);
            float hitChance;
            if (distance < ReferenceRange)
                hitChance = Mathf.Lerp(1f, chance, distance / ReferenceRange);
            else if (distance < _maxRange)
                hitChance = Mathf.Lerp(chance, 0f, (distance - ReferenceRange) / (_maxRange - ReferenceRange));
            else
                hitChance = 0f;

            return Random.value < hitChance;
        }

        /// <summary>
        /// Per damage type of the weapon: its damage (G2: plus the shooter's dexterity) minus the target's protection,
        /// immune types (protection -1) left out. G1 doubles it on a critical hit.
        /// </summary>
        public int CalculateDamage(NpcContainer shooter, NpcContainer target, ItemInstance weapon)
        {
            if (((int)target.Instance.Flags & _npcFlagImmortal) != 0)
                return 0;

            var isGothic2 = _contextGameVersionService.IsGothic2();
            var dexterity = isGothic2 ? shooter.Vob.GetAttribute((int)NpcAttribute.Dexterity) : 0;
            var mask = weapon.DamageType;
            var typeCount = 0;
            for (var i = 0; i < _damageTypeCount; i++)
                if ((mask & (1 << i)) != 0)
                    typeCount++;

            var damage = 0;
            for (var i = 0; i < _damageTypeCount; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;

                var protection = target.Vob.GetProtection(i);
                if (protection < 0)
                    continue;

                // Items give either a damage per type or a total spread over their types.
                var typeDamage = weapon.GetDamage((DamageType)i);
                if (typeDamage == 0 && typeCount > 0)
                    typeDamage = weapon.DamageTotal / typeCount;
                damage += Mathf.Max(dexterity + typeDamage - protection, 0);
            }

            if (!isGothic2 && Random.Range(0, 100) < GetTalentValue(shooter, weapon))
                damage *= GetCriticalMultiplier();

            return damage;
        }

        private float GetHitChance(NpcContainer shooter, ItemInstance weapon)
        {
            if (!_contextGameVersionService.IsGothic2())
                return shooter.Vob.GetAttribute((int)NpcAttribute.Dexterity) / 100f;

            var talent = IsCrossbow(weapon) ? NpcTalent.Crossbow : NpcTalent.Bow;
            return shooter.Instance.GetHitChance(talent) / 100f;
        }

        private int GetTalentValue(NpcContainer shooter, ItemInstance weapon)
        {
            var talent = IsCrossbow(weapon) ? NpcTalent.Crossbow : NpcTalent.Bow;
            return _npcHelperService.ExtNpcGetTalentValue(shooter.Instance, (int)talent);
        }

        private int GetCriticalMultiplier()
        {
            var symbol = _gameStateService.GothicVm.GetSymbolByName("DAM_CRITICAL_MULTIPLIER");
            return symbol != null ? Mathf.Max(1, symbol.GetInt(0)) : 2;
        }

        private static bool IsCrossbow(ItemInstance weapon)
        {
            return ((VmGothicEnums.ItemFlags)weapon.Flags).HasFlag(VmGothicEnums.ItemFlags.ItemCrossbow);
        }
    }
}
