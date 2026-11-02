using System.Collections.Generic;
using UnityEngine;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// VOBs that react to hits (reactToOnDamage: triggers, trigger scripts, movers) - the engine focuses them like NPCs
    /// with a bow/crossbow/spell drawn. Projectiles aim at them (VRProjectile auto-aim), they are often invisible.
    /// </summary>
    public static class DamageTargets
    {
        private static readonly HashSet<Transform> _targets = new();

        public static void Register(Transform target)
        {
            if (target != null)
                _targets.Add(target);
        }

        public static void Unregister(Transform target)
        {
            _targets.Remove(target);
        }

        /// <summary>
        /// The target closest to the direction from origin, within maxAngle degrees and maxDistance meters.
        /// </summary>
        public static Transform FindInCone(Vector3 origin, Vector3 direction, float maxAngle, float maxDistance)
        {
            Transform best = null;
            var bestAngle = maxAngle;
            _targets.RemoveWhere(t => t == null);
            foreach (var target in _targets)
            {
                if (!target.gameObject.activeInHierarchy)
                    continue;

                var toTarget = target.position - origin;
                if (toTarget.magnitude > maxDistance)
                    continue;

                var angle = Vector3.Angle(direction, toTarget);
                if (angle >= bestAngle)
                    continue;

                bestAngle = angle;
                best = target;
            }
            return best;
        }
    }
}
