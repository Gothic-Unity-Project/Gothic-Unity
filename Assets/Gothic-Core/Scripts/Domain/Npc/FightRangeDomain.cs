using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// The fight distances of the engine (OpenGothic FightAlgo): where an attacker stops running, which fight
    /// table (W-range) it uses, how far its strike reaches, and where AI_GotoNpc stops. All from the guild values
    /// (GIL_VALUES, Species.d) in cm, nothing scaled. Owned by FightService.
    /// </summary>
    public class FightRangeDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ContextRangedCombatService _contextRangedCombatService;


        /// <summary>
        /// OpenGothic FightAlgo::prefferedAttackDistance (fightalgo.cpp:246-251) in meters:
        /// FIGHT_RANGE_BASE[target] + FIGHT_RANGE_BASE[self] + weapon range of self. The W-range (isInWRange), the
        /// stop of the run at the enemy (GoTo::isClose GT_Enemy, npc.cpp:52-54), the strike's reach (commitDamage
        /// isInAttackRange, npc.cpp:2010) and AI_GotoNpc's stop (implGoTo, npc.cpp:1489-1493) all use it.
        /// Two unarmed humans: 60 + 60 + 50 = 1.7 m. No minimum - the engine has none.
        /// </summary>
        /// <param name="isMelee">A spell monster biting: its fist range instead of its spell's.</param>
        public float GetPreferredAttackDistance(NpcContainer self, NpcContainer target, bool isMelee = false)
        {
            var guildValues = _gameStateService.GuildValues;
            if (guildValues == null || self?.Instance == null || target?.Instance == null)
                return 0f;

            var baseRange = guildValues.GetFightRangeBase(target.Instance.GetValuesGuild()) +
                            guildValues.GetFightRangeBase(self.Instance.GetValuesGuild());
            return (baseRange + GetWeaponRangeCm(self, isMelee)) / 100f;
        }

        /// <summary>
        /// Horizontal distance (m) between the two NPCs' positions - what the preferred attack distance is compared
        /// with. (OpenGothic FightAlgo::qDistTo measures root to root in 3D; heights are handled by the melee height
        /// check, so the chase compares on the ground.)
        /// </summary>
        public float GetFightDistance(NpcContainer self, NpcContainer target)
        {
            var offset = target.Go.transform.position - self.Go.transform.position;
            return new Vector2(offset.x, offset.z).magnitude;
        }

        /// <summary>
        /// OpenGothic FightAlgo::weaponRange (fightalgo.cpp:335-362) in cm: FIGHT_RANGE_1HA/2HA[guild] + the drawn
        /// weapon's range (Item::swordLength = C_ITEM.range), FIGHT_RANGE_FIST[guild] without a weapon, the game's
        /// reference bow range for bows/crossbows (ReferenceBowRangeG1/G2) and MaxMagRange for spells.
        /// </summary>
        private float GetWeaponRangeCm(NpcContainer self, bool isMelee)
        {
            var guildValues = _gameStateService.GuildValues;
            var guild = self.Instance.GetValuesGuild();
            var weaponState = isMelee ? VmGothicEnums.WeaponState.Fist : (VmGothicEnums.WeaponState)self.Vob.FightMode;

            switch (weaponState)
            {
                case VmGothicEnums.WeaponState.W1H:
                    return guildValues.GetFightRange1Ha(guild) + GetDrawnWeaponRange(self);
                case VmGothicEnums.WeaponState.W2H:
                    return guildValues.GetFightRange2Ha(guild) + GetDrawnWeaponRange(self);
                case VmGothicEnums.WeaponState.Bow:
                case VmGothicEnums.WeaponState.CBow:
                    return _contextRangedCombatService.HitChanceReferenceRange * 100f;
                case VmGothicEnums.WeaponState.Mage:
                    return FightConst.MaxMagRangeMeters * 100f;
                default:
                    return guildValues.GetFightRangeFist(guild);
            }
        }

        private int GetDrawnWeaponRange(NpcContainer self)
        {
            return _vmCacheService.TryGetItemData(self.Props.CurrentItem)?.Range ?? 0;
        }
    }
}
