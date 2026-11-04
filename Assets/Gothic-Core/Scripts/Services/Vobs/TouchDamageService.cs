using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Vobs
{
    /// <summary>
    /// Damage zones (oCTouchDamage: fire pits, spikes, the addon temples' traps) like the engine (OpenGothic
    /// TouchDamage::tick): whoever stands in the zone loses its damage for each of its damage types minus his protection
    /// against that type, again after each repeat delay. An immune type (protection below 0) does nothing. No knockout,
    /// no perception: at 0 hit points the victim dies.
    /// </summary>
    public class TouchDamageService
    {
        [Inject] private readonly NpcService _npcService;

        public void Damage(NpcContainer victim, ITouchDamage zone)
        {
            if (victim?.Instance == null || victim.Props.IsDead)
                return;

            var zoneDamage = (int)zone.Damage;
            var damage = victim.ApplyProtection(GetDamageTypeMask(zone),
                (_, protection) => Mathf.Max(zoneDamage - protection, 0), out _);
            if (damage <= 0)
                return;

            Logger.Log($"[TouchDamage] {victim.Instance.GetName(NpcNameSlot.Slot0)} in '{zone.Name}': {damage} damage",
                LogCat.Vob);
            // Down like after a script taking the last hit points: one owner decides (FightService), nobody gets XP.
            _npcService.ReduceHitPoints(victim, damage);
        }

        /// <summary>
        /// The zone's damage types as a mask, bit index = DamageType (the protection index DAM_INDEX_*).
        /// </summary>
        private static int GetDamageTypeMask(ITouchDamage zone)
        {
            var mask = 0;
            if (zone.IsBarrier) mask |= 1 << (int)DamageType.Barrier;
            if (zone.IsBlunt) mask |= 1 << (int)DamageType.Blunt;
            if (zone.IsEdge) mask |= 1 << (int)DamageType.Edge;
            if (zone.IsFire) mask |= 1 << (int)DamageType.Fire;
            if (zone.IsFly) mask |= 1 << (int)DamageType.Fly;
            if (zone.IsMagic) mask |= 1 << (int)DamageType.Magic;
            if (zone.IsPoint) mask |= 1 << (int)DamageType.Point;
            if (zone.IsFall) mask |= 1 << (int)DamageType.Fall;
            return mask;
        }
    }
}
