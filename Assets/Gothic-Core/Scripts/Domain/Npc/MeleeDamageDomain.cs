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
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Player;
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
        [Inject] private readonly PlayerService _playerService;

        private int? _minimalDamage;
        // The hero swung a weapon without its requirements (CalculateMeleeDamage) - the hit does 1.
        private bool _isRequirementCapped;


        /// <summary>
        /// The hit takes the target's hit points. Returns true if they are gone - the caller decides: knocked out or
        /// dead. A spell or projectile passes its damage (damageOverride) - only the protection of its type
        /// (overrideProtectionIndex) is taken off.
        /// </summary>
        public bool ApplyHit(NpcContainer attacker, NpcContainer target, int? damageOverride = null,
            int overrideProtectionIndex = (int)DamageType.Fire)
        {
            var hitPoints = target.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            var maxHP = target.Vob.GetAttribute((int)NpcAttribute.HitPointsMax);

            int damage;
            bool isImmune;
            if (damageOverride.HasValue)
            {
                // FIXME: spells use their own damage type (overrideProtectionIndex stays Fire for them).
                var protection = target.Vob.GetProtection(overrideProtectionIndex);
                isImmune = protection < 0;
                damage = isImmune ? 0 : Mathf.Max(0, damageOverride.Value - protection);
            }
            else
            {
                damage = CalculateMeleeDamage(attacker, target, out isImmune);
            }

            // Like the engine, a hit never does less than NPC_MINIMAL_DAMAGE (Balancing.d, G1: 1) unless the target is
            // immune (-1) - a meatbug (fire protection 100) took 0 from a 50 damage fire storm and burned without
            // losing HP.
            if (!isImmune)
                damage = Mathf.Max(damage, GetMinimalDamage());

            // 1 also against the minimal damage (Mroczne Tajemnice: 5).
            if (_isRequirementCapped && !isImmune)
                damage = 1;
            _isRequirementCapped = false;

            var isTargetHero = target.Instance.IsHero(_gameStateService.GothicVm);
            // NPC_FLAG_IMMORTAL: take no damage, but combat still plays out normally. Marvin's god mode for the hero
            // likewise (the script flag is left alone - AI reads it).
            if (((int)target.Instance.Flags & FightConst.NpcFlagImmortal) != 0 || (isTargetHero && _playerService.IsGodMode))
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

        /// <summary>
        /// The protection (PROT_*) index of a damage type mask (DAM_*): its lowest bit, blunt without one. Spells and
        /// projectiles pass it to ApplyHit.
        /// </summary>
        public static int GetProtectionIndex(int damageTypeMask)
        {
            for (var i = 0; i < 8; i++)
            {
                if ((damageTypeMask & (1 << i)) != 0)
                    return i;
            }

            return (int)DamageType.Blunt;
        }

        /// <summary>
        /// C_ITEM.cond_atr[]/cond_value[]: the attributes a weapon needs (G1/G2: strength or dexterity).
        /// </summary>
        public static bool IsWeaponRequirementMet(NpcContainer npc, ItemInstance weapon, out string requirement,
            out int missingAttribute, out int requiredValue)
        {
            requirement = null;
            missingAttribute = -1;
            requiredValue = 0;
            foreach (ItemConditionSlot slot in System.Enum.GetValues(typeof(ItemConditionSlot)))
            {
                var attribute = weapon.GetCondAtr(slot);
                var value = weapon.GetCondValue(slot);
                if (value <= 0 || attribute < 0 || attribute >= 8)
                    continue;
                if (npc.Vob.GetAttribute(attribute) >= value)
                    continue;

                requirement = $"{(NpcAttribute)attribute} {value} (has {npc.Vob.GetAttribute(attribute)})";
                missingAttribute = attribute;
                requiredValue = value;
                return false;
            }
            return true;
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
        /// The hero's weapon is the one in his VR hand (Props.MeleeHitWeapon) - it isn't equipped in VR, he hit with
        /// his strength alone (Lukis in heavy armor took 30 instead of 80). Swung without its requirements
        /// (cond_atr/cond_value, e.g. 50 strength), every hit does only 1 damage.
        private int CalculateMeleeDamage(NpcContainer attacker, NpcContainer target, out bool isImmune)
        {
            var isHeroAttacker = attacker.Instance.IsHero(_gameStateService.GothicVm);
            var weapon = isHeroAttacker
                ? attacker.Props.MeleeHitWeapon
                : _npcHelperService.ExtNpcGetEquippedMeleeWeapon(attacker.Instance);
            // In fist mode the engine hits with the fist, not the sword in the belt.
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

            if (isHeroAttacker && weapon != null && !IsWeaponRequirementMet(attacker, weapon, out var requirement, out _,
                    out _))
            {
                Logger.Log($"[FightService] The hero lacks {requirement} for '{weapon.Name}' - {damage} damage capped " +
                           "to 1", LogCat.Fight);
                damage = Mathf.Min(damage, 1);
                _isRequirementCapped = true;
            }

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
