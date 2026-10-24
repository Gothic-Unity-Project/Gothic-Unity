using System;
using System.Collections.Generic;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Models.Container;
using Gothic.Core.Logging;
using Gothic.Core.Services.Npc;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// Flying arrow/bolt/spell, like the engine's "bullet" (OpenGothic world/bullet.cpp): moves itself (optional gravity,
    /// optional homing - Gothic's TARGET trajectory) and sphere-casts each step against the world and NPC hitboxes, so
    /// fast projectiles (30 m/s) can't tunnel through thin colliders. The shooter decides what a hit means (callbacks).
    /// Shot by the VR hero (bow, crossbow, spells) and by NPCs (ProjectileService.ShootAtTarget) - an NPC's projectile
    /// also hits the hero's body (Player layer), which has no NPC hitbox.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public NpcContainer Owner;
        public Vector3 Velocity;
        public bool UseGravity;
        [Tooltip("Share of the gravity applied with UseGravity (thrown spells: weaker throws fall more).")]
        public float GravityScale = 1f;
        [Tooltip("May lock on to a hit-reactive VOB in a small cone (Irdorath's bridge targets).")]
        public bool AllowAutoAim = true;
        public float Radius = 0.05f;
        public float MaxDistance = 60f;

        [Tooltip("Steers towards this NPC's upper body with HomingDegreesPerSecond (0 = straight flight).")]
        public NpcContainer HomingTarget;
        public float HomingDegreesPerSecond;

        // DeveloperConfig.EnableDamageTargetAutoAim: a VOB that reacts to hits (often invisible) in the flight direction.
        private Transform _homingVobTarget;
        private bool _isAutoAimChecked;
        private const float _autoAimConeDegrees = 12f;
        private const float _autoAimDegreesPerSecond = 120f;

        public Action<NpcContainer, Vector3, Vector3> OnNpcHit;
        public Action<Vector3, Vector3> OnWorldHit;
        public Action OnExpired;

        private const float _homingTargetHeight = 0.4f;
        private static readonly RaycastHit[] _hits = new RaycastHit[16];

        private static readonly (IDamageable damageable, float distance)[] _triggerHits =
            new (IDamageable, float)[16];
        private static int _triggerHitCount;

        private readonly HashSet<IDamageable> _damaged = new();
        private float _travelled;
        private bool _isDone;

        // The hero, if someone else shot this - his body (Player layer) can be hit. Null for the hero's own shots.
        private NpcContainer _heroTarget;
        private bool _isHeroTargetChecked;


        private void Update()
        {
            if (_isDone)
                return;

            var deltaTime = Time.deltaTime;
            // Locked on to a target, the homing carries it there - gravity would pull it under the target.
            if (UseGravity && !IsHoming)
                Velocity += Physics.gravity * (GravityScale * deltaTime);

            ApplyHoming(deltaTime);

            var step = Velocity * deltaTime;
            var distance = step.magnitude;
            if (distance <= 0f)
                return;

            var isHit = TryGetHit(step / distance, distance, out var hit, out var npc);
            DamageTriggersOnTheWay(isHit ? hit.distance : distance);

            if (isHit)
            {
                _isDone = true;
                transform.position = hit.distance > 0f ? hit.point : transform.position;
                if (npc != null)
                {
                    OnNpcHit?.Invoke(npc, transform.position, hit.normal);
                }
                else
                {
                    // A solid VOB can react too (zCMover with reactToOnDamage).
                    DamageOnce(hit.collider.GetComponentInParent<IDamageable>());
                    OnWorldHit?.Invoke(transform.position, hit.normal);
                }
                return;
            }

            transform.position += step;
            if (Velocity.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(Velocity);

            _travelled += distance;
            if (_travelled > MaxDistance)
            {
                _isDone = true;
                OnExpired?.Invoke();
            }
        }

        /// <summary>
        /// Triggers crossed in this step before the projectile stopped.
        /// </summary>
        private void DamageTriggersOnTheWay(float maxDistance)
        {
            for (var i = 0; i < _triggerHitCount; i++)
            {
                if (_triggerHits[i].distance <= maxDistance)
                    DamageOnce(_triggerHits[i].damageable);
            }
        }

        /// <summary>
        /// A trigger volume spans several steps - it reacts once per projectile.
        /// </summary>
        private void DamageOnce(IDamageable damageable)
        {
            if (damageable == null || !_damaged.Add(damageable))
                return;

            damageable.OnDamage(Owner);
        }

        /// <summary>
        /// Once, at the start of the flight: without an NPC to home on, the closest hit-reactive VOB within a small cone
        /// (G2 Irdorath's bridge targets: invisible, the engine focuses them with a bow drawn).
        /// </summary>
        private bool IsHoming => HomingDegreesPerSecond > 0f && (_homingVobTarget != null || HomingTarget?.Go != null);

        private void TryAutoAimAtDamageTarget()
        {
            _isAutoAimChecked = true;
            if (!AllowAutoAim || HomingTarget != null || Velocity.sqrMagnitude < 0.01f)
                return;

            var config = Gothic.Core.ReflexProjectInstaller.DIContainer.Resolve<Gothic.Core.Services.Config.ConfigService>();
            if (!config.Dev.EnableDamageTargetAutoAim)
                return;

            _homingVobTarget = DamageTargets.FindInCone(transform.position, Velocity, _autoAimConeDegrees, MaxDistance);
            if (_homingVobTarget == null)
                return;

            HomingDegreesPerSecond = Mathf.Max(HomingDegreesPerSecond, _autoAimDegreesPerSecond);
            Logger.Log($"[Projectile] Auto-aim at '{_homingVobTarget.name}' (reacts to hits)", LogCat.Vob);
        }

        private void ApplyHoming(float deltaTime)
        {
            if (!_isAutoAimChecked)
                TryAutoAimAtDamageTarget();

            if (_homingVobTarget != null && HomingDegreesPerSecond > 0f)
            {
                var toVob = _homingVobTarget.position - transform.position;
                if (toVob.sqrMagnitude >= 0.01f)
                    Velocity = Vector3.RotateTowards(Velocity, toVob.normalized * Velocity.magnitude,
                        HomingDegreesPerSecond * Mathf.Deg2Rad * deltaTime, 0f);
                return;
            }

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
        private NpcContainer GetHeroTarget()
        {
            if (_isHeroTargetChecked)
                return _heroTarget;
            _isHeroTargetChecked = true;

            var hero = ReflexProjectInstaller.DIContainer.Resolve<NpcService>().GetHeroContainer();
            _heroTarget = hero != null && hero != Owner ? hero : null;
            return _heroTarget;
        }

        private bool TryGetHit(Vector3 direction, float distance, out RaycastHit nearest, out NpcContainer npc)
        {
            nearest = default;
            npc = null;
            _triggerHitCount = 0;

            // Ignore Raycast only for VOBs that react to damage (oCTriggerScript, mover meshes) - never stops the flight.
            var mask = (1 << Constants.DefaultLayer) | (1 << Constants.VobHitbox) | (1 << Constants.IgnoreRaycastLayer);
            var heroTarget = GetHeroTarget();
            if (heroTarget != null)
                mask |= 1 << Constants.PlayerLayer;
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
                else if (hit.collider.gameObject.layer == Constants.PlayerLayer)
                {
                    // The hero's body (CharacterController, hands) - only when someone else shot.
                    if (heroTarget == null || hit.collider.isTrigger)
                        continue;
                    hitNpc = heroTarget;
                }
                else if (hit.collider.isTrigger || hit.collider.gameObject.layer == Constants.IgnoreRaycastLayer)
                {
                    // Flies through trigger volumes (and Ignore Raycast VOBs), but remembers them: zCTrigger & co.
                    // with reactToOnDamage fire when an arrow passes (e.g. the switches in the Sleeper's temple).
                    var damageable = hit.collider.GetComponentInParent<IDamageable>();
                    if (damageable != null && _triggerHitCount < _triggerHits.Length)
                        _triggerHits[_triggerHitCount++] = (damageable, hit.distance);
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
