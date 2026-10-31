using System;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// The engine's rules for a spell hitting an NPC (OpenGothic Npc::takeDamage, DamageCalculator::rangeDamage): whether
    /// it touches the victim - G1 by the spell's target type, G2 as the scripts' C_CanNpcCollideWithSpell says - and what
    /// it takes off: the spell's damage for each of its damage types minus the victim's protection (immune types left
    /// out), doubled or halved as the scripts say. No armor, strength or critical hits. Owned by MagicService
    /// and WorldEffectService.
    /// </summary>
    public class SpellDamageDomain
    {
        // COLL_* (G2 Constants.d).
        public const int CollDoNothing = 0;
        public const int CollDoEverything = 1;
        public const int CollApplyDamage = 2;
        public const int CollApplyHalvedDamage = 4;
        public const int CollApplyDoubledDamage = 8;
        public const int CollApplyVictimState = 16;
        public const int CollDontKill = 32;

        // TARGET_TYPE_* (C_Spell.targetCollectType).
        private const int _targetTypeAll = 1;
        private const int _targetTypeNpcs = 4;
        private const int _targetTypeOrcs = 8;
        private const int _targetTypeHumans = 16;
        private const int _targetTypeUndead = 32;

        private const int _damageTypeCount = 8;
        private const int _npcFlagImmortal = 2;
        // G2 spells take at least this off (OpenGothic DamageCalculator MinDamage).
        private const int _minDamageG2 = 5;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        /// <summary>
        /// The COLL_* mask of the spell on the victim.
        /// </summary>
        public int GetCollisionMask(NpcContainer caster, NpcContainer victim, int spellId, SpellInstance spell)
        {
            if (!_contextGameVersionService.IsGothic2())
                return spell != null && IsTargetable(victim, spell.TargetCollectType) ? CollDoEverything
                    : CollDoNothing;

            var vm = _gameStateService.GothicVm;
            var function = vm.GetSymbolByName("C_CanNpcCollideWithSpell");
            if (function == null || function.Type != DaedalusDataType.Function || function.IsExternal)
                return CollDoEverything;

            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = victim.Instance;
            vm.GlobalOther = caster.Instance;
            try
            {
                return vm.Call<int, int>(function.Index, spellId);
            }
            catch (Exception e)
            {
                Logger.LogError($"[Magic] C_CanNpcCollideWithSpell failed: {e.Message}", LogCat.Fight);
                return CollDoEverything;
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
            }
        }

        /// <param name="level">1-based. A projectile's damage grows with the level it was cast at, other spells' doesn't.</param>
        public int CalculateDamage(NpcContainer victim, SpellInstance spell, int level, int collisionMask)
        {
            if (spell == null || ((int)victim.Instance.Flags & _npcFlagImmortal) != 0)
                return 0;

            if ((collisionMask & (CollApplyDamage | CollApplyDoubledDamage | CollApplyHalvedDamage |
                                  CollDoEverything)) == 0)
                return 0;

            var typeDamage = spell.DamagePerLevel * Mathf.Max(level, 1);
            if ((collisionMask & CollApplyDoubledDamage) != 0)
                typeDamage *= 2;
            if ((collisionMask & CollApplyHalvedDamage) != 0)
                typeDamage /= 2;

            return CalculateDamage(victim, spell.DamageType, typeDamage);
        }

        /// <summary>
        /// The damage for each of the damage types (DAM_* bits) minus the victim's protection, immune types left out -
        /// also for the damage of a script effect (Wld_PlayEffect).
        /// </summary>
        public int CalculateDamage(NpcContainer victim, int damageType, int typeDamage)
        {
            if (((int)victim.Instance.Flags & _npcFlagImmortal) != 0)
                return 0;

            var damage = 0;
            var isImmune = true;
            for (var i = 0; i < _damageTypeCount; i++)
            {
                if ((damageType & (1 << i)) == 0 || typeDamage == 0)
                    continue;

                var protection = victim.Vob.GetProtection(i);
                if (protection < 0)
                    continue;

                isImmune = false;
                damage += Mathf.Max(typeDamage - protection, 0);
            }

            if (isImmune)
                return 0;

            return _contextGameVersionService.IsGothic2() ? Mathf.Max(damage, _minDamageG2) : damage;
        }

        /// <summary>
        /// Like the engine (OpenGothic Npc::isTargetableBySpell): all NPCs, or only humans, orcs or the undead.
        /// </summary>
        private bool IsTargetable(NpcContainer victim, int targetType)
        {
            if ((targetType & (_targetTypeAll | _targetTypeNpcs)) != 0)
                return true;

            var guild = victim.Instance.Guild;
            if ((targetType & _targetTypeHumans) != 0 && victim.IsHuman())
                return true;
            if ((targetType & _targetTypeOrcs) != 0 && guild > GetGuildConstant("GIL_SEPERATOR_ORC", 58))
                return true;

            return (targetType & _targetTypeUndead) != 0 && (guild == GetGuildConstant("GIL_ZOMBIE", -1) ||
                guild == GetGuildConstant("GIL_UNDEADORC", -1) || guild == GetGuildConstant("GIL_SKELETON", -1));
        }

        private int GetGuildConstant(string name, int fallback)
        {
            var symbol = _gameStateService.GothicVm.GetSymbolByName(name);
            return symbol != null && symbol.Type == DaedalusDataType.Int ? symbol.GetInt(0) : fallback;
        }
    }
}
