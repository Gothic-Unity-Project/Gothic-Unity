using System;
using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Object = UnityEngine.Object;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Arrows and bolts in flight, like the engine's bullets (OpenGothic World::shootBullet): 30 m/s across, on a
    /// ballistic arc aimed at the target. An arrow hits whoever it was shot at if it passes close to him - the VR player
    /// can step out of its way - and stops at the first wall or ground. A missed arrow stays a while, then vanishes.
    /// Owned by FightService.
    /// </summary>
    public class ArrowFlightDomain
    {
        private const float _speed = 30f;
        private const float _gravity = 9.81f;
        // An arrow passing this close to its target hits it.
        private const float _hitRadius = 0.45f;
        // An arrow flies at most this long, then falls out of the game.
        private const float _maxFlightSeconds = 3f;
        private const float _missedArrowSeconds = 15f;
        // Only NPCs this close to the shooter can be hit (the longest flight with some margin).
        private const float _candidateRange = _speed * _maxFlightSeconds + 5f;

        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        // FightService.OnRangedHit(shooter, target, weapon, flight distance).
        private readonly Action<NpcContainer, NpcContainer, ItemInstance, float> _onHit;
        private readonly int _blockingLayers;

        public ArrowFlightDomain(Action<NpcContainer, NpcContainer, ItemInstance, float> onHit)
        {
            _onHit = onHit;
            _blockingLayers = LayerMask.GetMask("Default");
        }

        /// <summary>
        /// An NPC's shot on a ballistic arc to the aim point. Like the engine (OpenGothic Bullet), it hits whoever it
        /// passes first, not only whom it was shot at (found on BoroBongo's branch).
        /// </summary>
        public void Launch(GameObject arrow, NpcContainer shooter, ItemInstance weapon, Vector3 start, Vector3 aimPoint)
        {
            _unityMonoService.StartCoroutine(Fly(arrow, shooter, GetCandidates(shooter, start), weapon, start,
                GetLaunchVelocity(start, aimPoint)));
        }

        private List<NpcContainer> GetCandidates(NpcContainer shooter, Vector3 start)
        {
            var candidates = new List<NpcContainer>();
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc == shooter || npc?.Go == null || !npc.Go.activeInHierarchy ||
                    npc.Props?.BodyState == VmGothicEnums.BodyState.BsDead)
                    continue;

                if ((npc.Go.transform.position - start).sqrMagnitude <= _candidateRange * _candidateRange)
                    candidates.Add(npc);
            }

            return candidates;
        }

        /// <summary>
        /// Horizontal speed fixed, the vertical part chosen so the arc ends at the aim point (OpenGothic shootBullet).
        /// </summary>
        private static Vector3 GetLaunchVelocity(Vector3 start, Vector3 aimPoint)
        {
            var toAim = aimPoint - start;
            var horizontal = new Vector3(toAim.x, 0f, toAim.z);
            var time = Mathf.Max(horizontal.magnitude / _speed, 0.05f);
            var velocity = horizontal / time;
            velocity.y = toAim.y / time + 0.5f * _gravity * time;
            return velocity;
        }

        private IEnumerator Fly(GameObject arrow, NpcContainer shooter, List<NpcContainer> targets,
            ItemInstance weapon, Vector3 start, Vector3 velocity)
        {
            var position = start;
            var travelled = 0f;
            for (var time = 0f; time < _maxFlightSeconds; time += Time.deltaTime)
            {
                yield return null;
                if (arrow == null)
                    yield break;

                var step = velocity * Time.deltaTime;
                velocity.y -= _gravity * Time.deltaTime;
                var next = position + step;

                var target = FindTarget(targets, position, next);
                if (target != null)
                {
                    _onHit(shooter, target, weapon, travelled + step.magnitude);
                    Object.Destroy(arrow);
                    yield break;
                }

                if (Physics.Linecast(position, next, out var hit, _blockingLayers, QueryTriggerInteraction.Ignore))
                {
                    Place(arrow, hit.point, step);
                    Object.Destroy(arrow, _missedArrowSeconds);
                    yield break;
                }

                travelled += step.magnitude;
                position = next;
                Place(arrow, position, step);
            }

            Object.Destroy(arrow);
        }

        private static NpcContainer FindTarget(List<NpcContainer> targets, Vector3 from, Vector3 to)
        {
            foreach (var target in targets)
            {
                if (target?.Go != null && target.Props?.BodyState != VmGothicEnums.BodyState.BsDead &&
                    DistanceToSegment(GetTargetPoint(target), from, to) < _hitRadius)
                    return target;
            }

            return null;
        }

        private static Vector3 GetTargetPoint(NpcContainer target)
        {
            return Services.Npc.FightService.GetAimPoint(target);
        }

        private static void Place(GameObject arrow, Vector3 position, Vector3 direction)
        {
            arrow.transform.position = position;
            if (direction.sqrMagnitude > 0.0001f)
                arrow.transform.rotation = Quaternion.LookRotation(direction);
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
