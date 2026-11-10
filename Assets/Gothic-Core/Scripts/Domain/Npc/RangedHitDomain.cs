using System;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// An arrow or bolt hit (OpenGothic DamageCalculator::rangeDamageValue): the game version's ranged damage
    /// (IContextRangedCombatService), scaled by the shot, minus the protection of the weapon's damage type
    /// (arrows/bolts: PROT_POINT). The hit itself goes through FightService (applyHit: attacker, target, position,
    /// damage, protection index).
    /// </summary>
    public class RangedHitDomain
    {
        [Inject] private readonly ContextRangedCombatService _contextRangedCombatService;

        private readonly Action<NpcContainer, NpcContainer, Vector3, int, int> _applyHit;


        public RangedHitDomain(Action<NpcContainer, NpcContainer, Vector3, int, int> applyHit)
        {
            _applyHit = applyHit;
        }

        /// <summary>
        /// The hit chance roll is skipped - a VR projectile that physically hit, hit.
        /// FIXME - G1 crit (TALENT_BOW/CROSSBOW value % chance) together with the melee talent formula.
        /// </summary>
        public void OnRangedHit(NpcContainer shooter, NpcContainer target, Vector3 pos,
            (ItemInstance weapon, float damageScale) shot)
        {
            var (weapon, damageScale) = shot;
            if (shooter == null || target == null || weapon == null)
                return;

            var damage = _contextRangedCombatService.GetRangedDamage(shooter, weapon);
            damage = Mathf.RoundToInt(damage * Mathf.Clamp01(damageScale));

            var protectionIndex = MeleeDamageDomain.GetProtectionIndex(weapon.DamageType);
            Logger.Log($"[RangedHitDomain] {shooter.Instance.GetName(NpcNameSlot.Slot0)} -> " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)} raw={damage} prot[{protectionIndex}]",
                LogCat.Fight);

            _applyHit(shooter, target, pos, damage, protectionIndex);
        }
    }
}
