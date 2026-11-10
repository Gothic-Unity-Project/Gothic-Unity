using Gothic.Core.Extensions;
using Gothic.Core.Services.Npc;
using UnityEngine;

namespace Gothic.Core.Adapters.Vob
{
    public class ProjectileFactory : IProjectileFactory
    {
        public IProjectile Create(GameObject go)
        {
            return go.AddComponent<Projectile>().Inject();
        }
    }
}
