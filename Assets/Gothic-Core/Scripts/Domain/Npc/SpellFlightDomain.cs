using System;
using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Spells flying like the engine's (OpenGothic World::shootSpell, DynamicWorld::spellSpeed): straight at 10 m/s from
    /// the caster's hand, at the target's body or where the VR hero points. A spell hits the first NPC it passes close to
    /// - never its caster - and stops at walls and the ground. Owned by MagicService.
    /// </summary>
    public class SpellFlightDomain
    {
        private const float _speed = 10f;
        // A spell passing this close to an NPC's body hits it.
        private const float _hitRadius = 0.5f;
        private const float _maxFlightSeconds = 5f;
        // Only NPCs this close to the caster can be hit (the flight's length with some margin).
        private const float _candidateRange = _speed * _maxFlightSeconds + 5f;

        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        // MagicService: the cast, the NPC hit (null at a wall or the end of the flight), where.
        private readonly Action<SpellCast, NpcContainer, Vector3> _onHit;
        private readonly int _blockingLayers;

        public SpellFlightDomain(Action<SpellCast, NpcContainer, Vector3> onHit)
        {
            _onHit = onHit;
            _blockingLayers = LayerMask.GetMask("Default");
        }

        /// <param name="visual">The flying particles, destroyed when the flight ends. Null to fly unseen.</param>
        public void Launch(SpellCast cast, GameObject visual, Vector3 start, Vector3 direction)
        {
            _unityMonoService.StartCoroutine(Fly(cast, visual, start, direction.normalized * _speed,
                GetCandidates(cast.Caster, start)));
        }

        private List<NpcContainer> GetCandidates(NpcContainer caster, Vector3 start)
        {
            var candidates = new List<NpcContainer>();
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc == caster || npc?.Go == null || !npc.Go.activeInHierarchy ||
                    npc.Props?.BodyState == VmGothicEnums.BodyState.BsDead)
                    continue;

                if ((npc.Go.transform.position - start).sqrMagnitude <= _candidateRange * _candidateRange)
                    candidates.Add(npc);
            }

            return candidates;
        }

        private IEnumerator Fly(SpellCast cast, GameObject visual, Vector3 start, Vector3 velocity,
            List<NpcContainer> candidates)
        {
            var position = start;
            for (var time = 0f; time < _maxFlightSeconds; time += Time.deltaTime)
            {
                yield return null;

                var next = position + velocity * Time.deltaTime;
                var victim = FindVictim(candidates, position, next);
                if (victim != null)
                {
                    End(visual);
                    _onHit(cast, victim, FightService.GetAimPoint(victim));
                    yield break;
                }

                if (Physics.Linecast(position, next, out var hit, _blockingLayers, QueryTriggerInteraction.Ignore))
                {
                    End(visual);
                    _onHit(cast, null, hit.point);
                    yield break;
                }

                position = next;
                if (visual != null)
                    visual.transform.SetPositionAndRotation(position, Quaternion.LookRotation(velocity));
            }

            End(visual);
        }

        private static NpcContainer FindVictim(List<NpcContainer> candidates, Vector3 from, Vector3 to)
        {
            foreach (var npc in candidates)
            {
                if (npc.Go == null || npc.Props?.BodyState == VmGothicEnums.BodyState.BsDead)
                    continue;

                if (DistanceToSegment(FightService.GetAimPoint(npc), from, to) < _hitRadius)
                    return npc;
            }

            return null;
        }

        private static void End(GameObject visual)
        {
            if (visual != null)
                Object.Destroy(visual);
        }

        private static float DistanceToSegment(Vector3 point, Vector3 from, Vector3 to)
        {
            var segment = to - from;
            var length = segment.sqrMagnitude;
            var t = length > 0f ? Mathf.Clamp01(Vector3.Dot(point - from, segment) / length) : 0f;
            return Vector3.Distance(point, from + segment * t);
        }
    }
}
