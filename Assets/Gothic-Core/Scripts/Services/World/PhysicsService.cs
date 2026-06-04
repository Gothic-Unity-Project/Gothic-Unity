using Gothic.Core.Adapters.Npc;
using UnityEngine;

namespace Gothic.Core.Services.World
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
