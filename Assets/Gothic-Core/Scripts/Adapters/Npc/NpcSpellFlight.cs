using System;
using UnityEngine;

namespace Gothic.Core.Adapters.Npc
{
    /// <summary>
    /// DeveloperConfig.EnableNpcSpellFx: the visible flight of an NPC's spell (its CAST key PFX) to the target. The hit
    /// happens when it reaches the target (OnArrive), like in the engine, not the moment the cast animation reaches its
    /// frame. DeveloperConfig.NpcSpellHoming: how much it follows the target - at 0.2 it flies mostly to where the target
    /// was at the cast, a step aside dodges it (it flies on and fades); fully homing nobody could dodge a crawler's web.
    /// </summary>
    public class NpcSpellFlight : MonoBehaviour
    {
        private const float _targetHeight = 1f;
        private const float _hitRadius = 0.8f;
        private const float _fadeSeconds = 1f;

        public Transform Target;
        public float Speed = 15f;
        public float MaxSeconds = 4f;
        public float Homing = 1f;
        public Action OnArrive;

        private float _endTime;
        private bool _isDone;
        private Vector3 _castTargetPosition;
        private Vector3 _direction;
        private bool _isPastAimPoint;

        private void Start()
        {
            _endTime = Time.time + MaxSeconds;
            if (Target != null)
                _castTargetPosition = Target.position + Vector3.up * _targetHeight;
            _direction = (_castTargetPosition - transform.position).normalized;
        }

        private void Update()
        {
            if (_isDone)
                return;

            // The target is gone (despawned) or the spell flew too long: it fades out without a hit.
            if (Target == null || Time.time >= _endTime)
            {
                Fade(false);
                return;
            }

            var targetPosition = Target.position + Vector3.up * _targetHeight;
            if (Vector3.Distance(transform.position, targetPosition) <= _hitRadius)
            {
                Fade(true);
                return;
            }

            // Until it passes its aim point it steers to a mix of the cast position and the target's current one.
            if (!_isPastAimPoint)
            {
                var aimPoint = Vector3.Lerp(_castTargetPosition, targetPosition, Mathf.Clamp01(Homing));
                var toAim = aimPoint - transform.position;
                if (Vector3.Dot(toAim, _direction) <= 0f || toAim.magnitude <= Speed * Time.deltaTime)
                    _isPastAimPoint = true;
                else
                    _direction = toAim.normalized;
            }

            transform.SetPositionAndRotation(transform.position + _direction * (Speed * Time.deltaTime),
                Quaternion.LookRotation(_direction));
        }

        private void Fade(bool isHit)
        {
            _isDone = true;
            try
            {
                if (isHit)
                    OnArrive?.Invoke();
            }
            finally
            {
                foreach (var particles in GetComponentsInChildren<ParticleSystem>(true))
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(gameObject, _fadeSeconds);
            }
        }
    }
}
