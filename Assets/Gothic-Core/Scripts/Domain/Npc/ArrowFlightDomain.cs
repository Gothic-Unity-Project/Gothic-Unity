using System;
using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Vobs;
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

        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly VobService _vobService;

        // FightService.OnRangedHit(shooter, target, weapon, flight distance).
        private readonly Action<NpcContainer, NpcContainer, ItemInstance, float> _onHit;
        private readonly int _blockingLayers;

        public ArrowFlightDomain(Action<NpcContainer, NpcContainer, ItemInstance, float> onHit)
        {
            _onHit = onHit;
            _blockingLayers = LayerMask.GetMask("Default");
        }

        public void Launch(GameObject arrow, NpcContainer shooter, NpcContainer target, ItemInstance weapon,
            Vector3 start, Vector3 aimPoint)
        {
            _unityMonoService.StartCoroutine(Fly(arrow, shooter, target, weapon, start, GetLaunchVelocity(start,
                aimPoint)));
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

        private IEnumerator Fly(GameObject arrow, NpcContainer shooter, NpcContainer target, ItemInstance weapon,
            Vector3 start, Vector3 velocity)
        {
            var position = start;
            var travelled = 0f;
            var hitZones = new HashSet<Collider>();
            for (var time = 0f; time < _maxFlightSeconds; time += Time.deltaTime)
            {
                yield return null;
                if (arrow == null)
                    yield break;

                var step = velocity * Time.deltaTime;
                velocity.y -= _gravity * Time.deltaTime;
                var next = position + step;

                if (target?.Go != null && DistanceToSegment(GetTargetPoint(target), position, next) < _hitRadius)
                {
                    _onHit(shooter, target, weapon, travelled + step.magnitude);
                    Object.Destroy(arrow);
                    yield break;
                }

                _vobService.DispatchDamageAlong(position, next, hitZones);
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
