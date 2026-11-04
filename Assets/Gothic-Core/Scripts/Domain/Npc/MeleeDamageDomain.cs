using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// The damage of a melee hit (OpenGothic DamageCalculator::swordDamage): roll, damage types, protection, minimal
    /// damage, immortality. The game version's formula comes from IMeleeDamageRule (G1/G2 module).
    /// </summary>
    public class MeleeDamageDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly ContextMeleeDamageRuleService _meleeDamageRule;

        private int? _minimalDamage;


        /// <summary>
        /// The hit takes the target's hit points. Returns true if they are gone - the caller decides: knocked out or
        /// dead.
        /// </summary>
        public bool ApplyHit(NpcContainer attacker, NpcContainer target)
        {
            var hitPoints = target.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            var maxHP = target.Vob.GetAttribute((int)NpcAttribute.HitPointsMax);

            var damage = CalculateMeleeDamage(attacker, target, out var isImmune);

            // Like the engine, a hit never does less than NPC_MINIMAL_DAMAGE (Balancing.d, G1: 1) unless the target is
            // immune (-1).
            if (!isImmune)
                damage = Mathf.Max(damage, GetMinimalDamage());

            // NPC_FLAG_IMMORTAL: take no damage, but combat still plays out normally.
            if (((int)target.Instance.Flags & FightConst.NpcFlagImmortal) != 0)
            {
                Logger.Log($"[FightService] {target.Instance.GetName(NpcNameSlot.Slot0)} is immortal — 0 damage",
                    LogCat.Npc);
                damage = 0;
            }

            Logger.Log($"[FightService.OnHitUpdateHealth] {target.Instance.GetName(NpcNameSlot.Slot0)}: " +
                       $"{hitPoints} - {damage} dmg", LogCat.Npc);

            hitPoints -= damage;

            Logger.Log($"[FightService.OnHitUpdateHealth] {target.Instance.GetName(NpcNameSlot.Slot0)} HP after: " +
                       $"{hitPoints}/{maxHP}", LogCat.Npc);

            // Health bars follow via GlobalEventDispatcher.NpcAttributeChanged.
            _npcService.SetAttribute(target, NpcAttribute.HitPoints, hitPoints);

            return hitPoints <= 0;
        }

        private int GetMinimalDamage()
        {
            _minimalDamage ??= _gameStateService.GothicVm.GetSymbolByName("NPC_MINIMAL_DAMAGE")?.GetInt(0) ?? 0;
            return _minimalDamage.Value;
        }

        /// <summary>
        /// Like the engine (OpenGothic DamageCalculator::swordDamage). Per damage type of the weapon (or the NPC's own
        /// damagetype: fists, claws - wolves bite DAM_EDGE): strength + the NPC's damage[] + the weapon's damage[] and
        /// damageTotal - the target's protection of that type; immune types (protection -1) are left out.
        /// The dice roll's meaning and the formula of one type are the game version's (IMeleeDamageRule).
        /// </summary>
        private int CalculateMeleeDamage(NpcContainer attacker, NpcContainer target, out bool isImmune)
        {
            var weapon = _npcHelperService.ExtNpcGetEquippedMeleeWeapon(attacker.Instance);
            if ((VmGothicEnums.WeaponState)attacker.Vob.FightMode == VmGothicEnums.WeaponState.Fist)
                weapon = null;

            var mask = weapon?.DamageType ?? attacker.Instance.DamageType;
            if (mask == 0)
                mask = 1 << (int)DamageType.Blunt;

            var isTwoHanded = weapon != null && ((VmGothicEnums.ItemFlags)weapon.Flags &
                (VmGothicEnums.ItemFlags.Item2HdSwd | VmGothicEnums.ItemFlags.Item2HdAxe)) != 0;
            var diceRoll = Random.Range(0, 100);
            var isRollSuccess = _meleeDamageRule.IsRollSuccess(attacker, weapon, isTwoHanded, diceRoll);
            var strength = attacker.Vob.GetAttribute((int)NpcAttribute.Strength);

            var damage = target.ApplyProtection(mask, (type, protection) =>
            {
                var typeDamage = attacker.Instance.GetDamage((DamageType)type) +
                                 (weapon != null ? weapon.GetDamage((DamageType)type) + weapon.DamageTotal : 0);
                return _meleeDamageRule.TypeDamage(strength, typeDamage, protection, isRollSuccess);
            }, out isImmune);

            Logger.Log($"[FightService.MeleeDamage] {attacker.Instance.GetName(NpcNameSlot.Slot0)} -> " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)}: weapon '{weapon?.Name ?? "none"}' " +
                       $"mask {mask}, " +
                       $"strength {strength}, {_meleeDamageRule.GetRollResultName(isRollSuccess)} " +
                       $"(dice {diceRoll}) = {damage}" +
                       $"{(isImmune ? " (immune)" : "")}", LogCat.Fight);
            return damage;
        }
    }
}
