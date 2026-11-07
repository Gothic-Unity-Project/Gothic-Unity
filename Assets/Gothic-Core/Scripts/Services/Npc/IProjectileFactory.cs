using UnityEngine;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Creates a flying arrow/bolt. Implemented in Adapters/, registered in ReflexProjectInstaller.
    /// </summary>
    public interface IProjectileFactory
    {
        /// <summary>
        /// Adds the flight to the given GameObject.
        /// </summary>
        IProjectile Create(GameObject go);
    }
}
