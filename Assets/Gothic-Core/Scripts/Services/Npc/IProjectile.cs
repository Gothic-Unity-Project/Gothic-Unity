using System;
using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// A flying arrow/bolt or NPC spell (the Projectile adapter). Services create it with IProjectileFactory and only
    /// decide what a hit means (callbacks).
    /// </summary>
    public interface IProjectile
    {
        NpcContainer Owner { get; set; }
        Vector3 Velocity { get; set; }
        bool UseGravity { get; set; }
        float MaxDistance { get; set; }
        float Radius { get; set; }
        bool AllowAutoAim { get; set; }

        /// <summary>
        /// Hit NPC, hit position, flight direction.
        /// </summary>
        Action<NpcContainer, Vector3, Vector3> OnNpcHit { get; set; }

        /// <summary>
        /// Hit position, surface normal.
        /// </summary>
        Action<Vector3, Vector3> OnWorldHit { get; set; }

        Action OnExpired { get; set; }
    }
}
