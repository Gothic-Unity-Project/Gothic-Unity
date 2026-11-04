using Gothic.Core.Models.Container;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.G1.Services.Fight
{
    /// <summary>
    /// G1 (OpenGothic DamageCalculator::swordDamage): a critical hit with the weapon talent's value as chance (1H/2H,
    /// only with a weapon) multiplies the damage by DAM_CRITICAL_MULTIPLIER.
    /// </summary>
    public class G1MeleeDamageRule : IMeleeDamageRule
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcHelperService _npcHelperService;

        private int? _criticalMultiplier;


        public bool IsRollSuccess(NpcContainer attacker, ItemInstance weapon, bool isTwoHanded, int diceRoll)
        {
            return weapon != null && GetCriticalChance(attacker, GetWeaponTalentId(isTwoHanded)) > diceRoll;
        }

        public int TypeDamage(int strength, int typeDamage, int protection, bool isRollSuccess)
        {
            var damage = isRollSuccess ? GetCriticalMultiplier() * typeDamage : typeDamage;

            return Mathf.Max(strength + damage - protection, 0);
        }

        public string GetRollResultName(bool isRollSuccess)
        {
            return isRollSuccess ? "CRITICAL" : "normal";
        }

        /// <summary>
        /// The scripts' talent id of the weapon (NPC_TALENT_1H / NPC_TALENT_2H).
        /// </summary>
        private int GetWeaponTalentId(bool isTwoHanded)
        {
            return _gameStateService.GothicVm.GetSymbolByName(isTwoHanded ? "NPC_TALENT_2H" : "NPC_TALENT_1H")
                ?.GetInt(0) ?? (isTwoHanded ? 2 : 1);
        }

        /// <summary>
        /// G1's critical hit chance: the weapon talent's value.
        /// </summary>
        private int GetCriticalChance(NpcContainer attacker, int talentId)
        {
            return _npcHelperService.ExtNpcGetTalentValue(attacker.Instance, talentId);
        }

        private int GetCriticalMultiplier()
        {
            _criticalMultiplier ??=
                _gameStateService.GothicVm.GetSymbolByName("DAM_CRITICAL_MULTIPLIER")?.GetInt(0) ?? 2;
            return _criticalMultiplier.Value;
        }
    }
}
