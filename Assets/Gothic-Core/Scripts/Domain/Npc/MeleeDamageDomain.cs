using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// The engine's melee rules (OpenGothic DamageCalculator, Npc::checkHealth): how far a weapon reaches, how much a
    /// hit takes off and whether a beaten NPC is knocked out or killed. Owned by FightService.
    /// </summary>
    public class MeleeDamageDomain
    {
        /// <summary>
        /// The bodies' volume: fights measure from the NPCs' roots, the weapons reach from their bodies.
        /// </summary>
        public const float BodyVolume = 0.3f;

        // NPCs without fight values in their guild looped in G-Range forever.
        private const float _minMeleeReach = 1.5f;
        private const int _npcFlagImmortal = 2;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly ContextMeleeDamageService _contextMeleeDamageService;

        /// <summary>
        /// Fight range base + the drawn melee weapon's range (G1 has no FIGHT_RANGE_1HA/2HA guild values), else the
        /// fists'. The fight AI picks its moves by it (Attack) and a swing hits within it (AttackPlayAni).
        /// </summary>
        public float GetMeleeReach(NpcContainer npc)
        {
            var guildValues = _gameStateService.GuildValues;
            var baseRange = guildValues.GetFightRangeBase(npc.Vob.GuildTrue);

            // CurrentItem can be the last used item (a novice's joint, range 0) without a weapon drawn.
            var item = ((VmGothicEnums.WeaponState)npc.Vob.FightMode).IsMelee()
                ? _vmCacheService.TryGetItemData(npc.Props.CurrentItem)
                : null;
            var weaponRange = item?.Range ?? guildValues.GetFightRangeFist(npc.Vob.GuildTrue);
            return Mathf.Max((baseRange + weaponRange) / 100f, _minMeleeReach);
        }

        /// <summary>
        /// OpenGothic DamageCalculator::swordDamage. Per damage type of the weapon (or the NPC's own damagetype: fists,
        /// claws): strength + the NPC's damage[] + the weapon's damage[] and damageTotal - the target's protection of
        /// that type, the game's part by IContextMeleeDamageService. Immune types (protection -1) are left out. A hit
        /// never does less than NPC_MINIMAL_DAMAGE unless the target is immune; NPC_FLAG_IMMORTAL takes nothing.
        /// </summary>
        /// <param name="heroWeapon">The weapon in the VR hero's hand: it isn't equipped in VR.</param>
        public int CalculateDamage(NpcContainer attacker, NpcContainer target, ItemInstance heroWeapon)
        {
            var weapon = attacker.IsHero() && heroWeapon != null
                ? heroWeapon
                : _npcHelperService.ExtNpcGetEquippedMeleeWeapon(attacker.Instance);
            // Fighting with fists, the sword in the belt doesn't count.
            if ((VmGothicEnums.WeaponState)attacker.Vob.FightMode == VmGothicEnums.WeaponState.Fist)
                weapon = null;

            var mask = weapon?.DamageType ?? attacker.Instance.DamageType;
            if (mask == 0)
                mask = 1 << (int)DamageType.Blunt;

            var isTwoHanded = weapon != null && ((VmGothicEnums.ItemFlags)weapon.Flags &
                (VmGothicEnums.ItemFlags.Item2HdSwd | VmGothicEnums.ItemFlags.Item2HdAxe)) != 0;
            var isFullHit = _contextMeleeDamageService.IsFullHit(attacker, weapon, isTwoHanded, Random.Range(0, 100));
            var strength = attacker.Vob.GetAttribute((int)NpcAttribute.Strength);

            var damage = 0;
            var isImmune = true;
            for (var i = 0; i < 8; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;

                var protection = target.Vob.GetProtection(i);
                if (protection < 0)
                    continue;

                var typeDamage = attacker.Instance.GetDamage((DamageType)i) +
                                 (weapon != null ? weapon.GetDamage((DamageType)i) + weapon.DamageTotal : 0);
                damage += _contextMeleeDamageService.GetTypeDamage(strength, typeDamage, protection, isFullHit);
                isImmune = false;
            }

            if (!isImmune)
                damage = Mathf.Max(damage, GetMinimalDamage());

            if (((int)target.Instance.Flags & _npcFlagImmortal) != 0)
                damage = 0;

            return damage;
        }

        private int GetMinimalDamage()
        {
            return _gameStateService.GothicVm.GetSymbolByName("NPC_MINIMAL_DAMAGE")?.GetInt(0) ?? 0;
        }

        /// <summary>
        /// The engine's rule (OpenGothic Npc::checkHealth): a human beaten in melee is knocked out unless it's hostile
        /// to its attacker - permanent attitude to the hero, the guilds' attitude between NPCs. Bandits die, citizens
        /// get up again. Monsters and orcs always die, so does anyone shot by an archer.
        /// </summary>
        public bool IsKnockedOutNotKilled(NpcContainer attacker, NpcContainer target)
        {
            if (!target.IsHuman())
                return false;

            if (!attacker.IsHero() && ((VmGothicEnums.WeaponState)attacker.Vob.FightMode).IsRanged())
                return false;

            var attitude = _npcAiService.ExtGetPermAttitude(target.Instance, attacker.Instance);
            return attitude != VmGothicEnums.Attitude.Hostile;
        }
    }
}
