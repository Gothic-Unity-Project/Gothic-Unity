using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Models.Vm;
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
        // DAM_INDEX_* order of the zone's damage types.
        private const int _indexBarrier = 0;
        private const int _indexBlunt = 1;
        private const int _indexEdge = 2;
        private const int _indexFire = 3;
        private const int _indexFly = 4;
        private const int _indexMagic = 5;
        private const int _indexPoint = 6;
        private const int _indexFall = 7;

        [Inject] private readonly NpcService _npcService;

        public void Damage(NpcContainer victim, ITouchDamage zone)
        {
            if (victim?.Instance == null || victim.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                return;

            var damage = 0;
            AddDamage(ref damage, victim, zone.IsBarrier, _indexBarrier, zone.Damage);
            AddDamage(ref damage, victim, zone.IsBlunt, _indexBlunt, zone.Damage);
            AddDamage(ref damage, victim, zone.IsEdge, _indexEdge, zone.Damage);
            AddDamage(ref damage, victim, zone.IsFire, _indexFire, zone.Damage);
            AddDamage(ref damage, victim, zone.IsFly, _indexFly, zone.Damage);
            AddDamage(ref damage, victim, zone.IsMagic, _indexMagic, zone.Damage);
            AddDamage(ref damage, victim, zone.IsPoint, _indexPoint, zone.Damage);
            AddDamage(ref damage, victim, zone.IsFall, _indexFall, zone.Damage);
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
