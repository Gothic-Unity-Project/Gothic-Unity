using Gothic.Core.Models.Container;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;

namespace Gothic.G1.Services.Fight
{
    /// <summary>
    /// Mroczne Tajemnice's percent system around the G1 rule: its teachers (B_GiveprocentSkill) keep a weapon's
    /// percent in another talent's value - the one its status menu shows as the critical hit chance. The higher one
    /// counts: a hero trained by Cavalorn had his bow percent in NPC_TALENT_FIREMASTER and never hit critically.
    /// </summary>
    public class G1ModPercentTalentMeleeDamageRule : IMeleeDamageRule
    {
        private const string _percentSkillFunction = "B_GIVEPROCENTSKILL";

        private static readonly (string Weapon, string Percent)[] _percentTalents =
        {
            ("NPC_TALENT_1H", "NPC_TALENT_SNEAK"),
            ("NPC_TALENT_2H", "NPC_TALENT_REGENERATE"),
            ("NPC_TALENT_BOW", "NPC_TALENT_FIREMASTER"),
            ("NPC_TALENT_CROSSBOW", "NPC_TALENT_ACROBAT")
        };

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcHelperService _npcHelperService;

        private readonly G1MeleeDamageRule _rule;


        public G1ModPercentTalentMeleeDamageRule(G1MeleeDamageRule rule)
        {
            _rule = rule;
        }

        /// <summary>
        /// The mod's scripts have the percent system.
        /// </summary>
        public static bool IsUsedBy(DaedalusVm vm)
        {
            return vm?.GetSymbolByName(_percentSkillFunction) != null;
        }

        public bool RollHit(NpcContainer attacker, ItemInstance weapon, bool isTwoHanded, int roll)
        {
            return weapon != null && GetCriticalChance(attacker, _rule.GetWeaponTalentId(isTwoHanded)) > roll;
        }

        public int TypeDamage(int strength, int typeDamage, int protection, bool isHit)
        {
            return _rule.TypeDamage(strength, typeDamage, protection, isHit);
        }

        public string GetRollName(bool isHit)
        {
            return _rule.GetRollName(isHit);
        }

        private int GetCriticalChance(NpcContainer attacker, int talentId)
        {
            var chance = _rule.GetCriticalChance(attacker, talentId);
            var vm = _gameStateService.GothicVm;
            foreach (var (weapon, percent) in _percentTalents)
            {
                if (vm.GetSymbolByName(weapon)?.GetInt(0) != talentId)
                    continue;
                var percentTalent = vm.GetSymbolByName(percent);
                if (percentTalent != null)
                    chance = Mathf.Max(chance,
                        _npcHelperService.ExtNpcGetTalentValue(attacker.Instance, percentTalent.GetInt(0)));
                break;
            }
            return chance;
        }
    }
}
