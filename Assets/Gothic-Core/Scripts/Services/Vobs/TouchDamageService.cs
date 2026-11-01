using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
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

            var damage = 0;
            // The protection index of each damage type (DAM_INDEX_*).
            AddDamage(ref damage, victim, zone.IsBarrier, (int)DamageType.Barrier, zone.Damage);
            AddDamage(ref damage, victim, zone.IsBlunt, (int)DamageType.Blunt, zone.Damage);
            AddDamage(ref damage, victim, zone.IsEdge, (int)DamageType.Edge, zone.Damage);
            AddDamage(ref damage, victim, zone.IsFire, (int)DamageType.Fire, zone.Damage);
            AddDamage(ref damage, victim, zone.IsFly, (int)DamageType.Fly, zone.Damage);
            AddDamage(ref damage, victim, zone.IsMagic, (int)DamageType.Magic, zone.Damage);
            AddDamage(ref damage, victim, zone.IsPoint, (int)DamageType.Point, zone.Damage);
            AddDamage(ref damage, victim, zone.IsFall, (int)DamageType.Fall, zone.Damage);
            if (damage <= 0)
                return;

            var hitPoints = victim.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            Logger.Log($"[TouchDamage] {victim.Instance.GetName(NpcNameSlot.Slot0)} in '{zone.Name}': {damage} damage",
                LogCat.Vob);
            _npcService.SetAttribute(victim, NpcAttribute.HitPoints, Mathf.Max(0, hitPoints - damage));
        }

        private static void AddDamage(ref int damage, NpcContainer victim, bool hasType, int index, float zoneDamage)
        {
            if (!hasType)
                return;

            var protection = victim.Vob.GetProtection(index);
            if (protection < 0)
                return;

            damage += Mathf.Max((int)zoneDamage - protection, 0);
        }
    }
}
