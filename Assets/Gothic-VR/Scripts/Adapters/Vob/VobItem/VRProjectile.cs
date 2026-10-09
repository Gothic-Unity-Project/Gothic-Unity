#if GOTHIC_HVR_INSTALLED
using System;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// Flying arrow/bolt/spell, like the engine's "bullet" (OpenGothic world/bullet.cpp): moves itself (optional gravity,
    /// optional homing - Gothic's TARGET trajectory) and sphere-casts each step against the world and NPC hitboxes, so
    /// fast projectiles (30 m/s) can't tunnel through thin colliders. The shooter decides what a hit means (callbacks).
    /// </summary>
    public class VRProjectile : MonoBehaviour
    {
        public NpcContainer Owner;
        public Vector3 Velocity;
        public bool UseGravity;
        public float Radius = 0.05f;
        public float MaxDistance = 60f;

        [Tooltip("Steers towards this NPC's upper body with HomingDegreesPerSecond (0 = straight flight).")]
        public NpcContainer HomingTarget;
        public float HomingDegreesPerSecond;

        public Action<NpcContainer, Vector3, Vector3> OnNpcHit;
        public Action<Vector3, Vector3> OnWorldHit;
        public Action OnExpired;

        private const float _homingTargetHeight = 0.4f;
        private static readonly RaycastHit[] _hits = new RaycastHit[16];

        private float _travelled;
        private bool _isDone;


        private void Update()
        {
            if (_isDone)
                return;

            var deltaTime = Time.deltaTime;
            if (UseGravity)
                Velocity += Physics.gravity * deltaTime;

            ApplyHoming(deltaTime);

            var step = Velocity * deltaTime;
            var distance = step.magnitude;
            if (distance <= 0f)
                return;

            if (TryGetHit(step / distance, distance, out var hit, out var npc))
            {
                _isDone = true;
                transform.position = hit.distance > 0f ? hit.point : transform.position;
                if (npc != null)
                    OnNpcHit?.Invoke(npc, transform.position, hit.normal);
                else
                    OnWorldHit?.Invoke(transform.position, hit.normal);
                return;
            }

            transform.position += step;
            transform.rotation = Quaternion.LookRotation(Velocity);

            _travelled += distance;
            if (_travelled > MaxDistance)
            {
                _isDone = true;
                OnExpired?.Invoke();
            }
        }

        private void ApplyHoming(float deltaTime)
        {
            if (HomingTarget?.Go == null || HomingDegreesPerSecond <= 0f)
                return;

            var targetPos = HomingTarget.Go.transform.position + Vector3.up * _homingTargetHeight;
            var toTarget = targetPos - transform.position;
            if (toTarget.sqrMagnitude < 0.01f)
                return;

            Velocity = Vector3.RotateTowards(Velocity, toTarget.normalized * Velocity.magnitude,
                HomingDegreesPerSecond * Mathf.Deg2Rad * deltaTime, 0f);
        }

        /// <summary>
        /// Nearest hit along the step: NPC hitboxes (triggers on VobHitbox) or solid world geometry (Default).
        /// The shooter's own hitbox and other trigger volumes are skipped.
        /// </summary>
        private bool TryGetHit(Vector3 direction, float distance, out RaycastHit nearest, out NpcContainer npc)
        {
            nearest = default;
            npc = null;

            var mask = (1 << Constants.DefaultLayer) | (1 << Constants.VobHitbox);
            var count = Physics.SphereCastNonAlloc(transform.position, Radius, direction, _hits, distance, mask,
                QueryTriggerInteraction.Collide);

            var found = false;
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                NpcContainer hitNpc = null;

                if (hit.collider.gameObject.layer == Constants.VobHitbox)
                {
                    hitNpc = hit.collider.GetComponentInParent<NpcLoader>()?.Container;
                    if (hitNpc == null || hitNpc == Owner)
                        continue;
                }
                else if (hit.collider.isTrigger)
                {
                    continue;
                }

                if (found && hit.distance >= nearest.distance)
                    continue;

                nearest = hit;
                npc = hitNpc;
                found = true;
            }

            return found;
        }
    }
}
#endif
