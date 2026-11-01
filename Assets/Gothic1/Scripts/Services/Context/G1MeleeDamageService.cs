using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.G1
{
    /// <summary>
    /// G1 melee damage (OpenGothic DamageCalculator::swordDamage): a critical hit, with the chance of the weapon's
    /// talent value, doubles the weapon's part (DAM_CRITICAL_MULTIPLIER).
    /// </summary>
    public class G1MeleeDamageService : IContextMeleeDamageService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcHelperService _npcHelperService;

        public bool IsFullHit(NpcContainer attacker, ItemInstance weapon, bool isTwoHanded, int roll)
        {
            if (weapon == null)
                return false;

            var talent = _gameStateService.GothicVm.GetSymbolByName(isTwoHanded ? "NPC_TALENT_2H" : "NPC_TALENT_1H");
            var talentId = talent?.GetInt(0) ?? (isTwoHanded ? 2 : 1);
            return _npcHelperService.ExtNpcGetTalentValue(attacker.Instance, talentId) > roll;
        }

        public int GetTypeDamage(int strength, int typeDamage, int protection, bool isFullHit)
        {
            var multiplier = _gameStateService.GothicVm.GetSymbolByName("DAM_CRITICAL_MULTIPLIER")?.GetInt(0) ?? 2;
            return Mathf.Max(strength + (isFullHit ? multiplier * typeDamage : typeDamage) - protection, 0);
        }
    }
}
