using GUZ.Core.Adapters.Npc;
using UnityEngine;

namespace GUZ.Core.Services.World
{
    public class PhysicsService
    {
        public void DisablePhysicsForNpc(NpcPrefabProperties prefabProps)
        {
            prefabProps.ColliderRootMotion.GetComponent<Rigidbody>().isKinematic = true;
        }

        public void EnablePhysicsForNpc(NpcPrefabProperties prefabProps)
        {
            prefabProps.ColliderRootMotion.GetComponent<Rigidbody>().isKinematic = false;
        }
    }
}
