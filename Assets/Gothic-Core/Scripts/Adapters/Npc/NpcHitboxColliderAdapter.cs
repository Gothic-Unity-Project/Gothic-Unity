using Gothic.Core.Adapters.Npc;
using Gothic.Core.Extensions;
using UnityEngine;

namespace Gothic.VR.Adapters.Npc
{
    // TODO - Could be subsummarized and filled in NpcMeshBuilder .center/.size instead of a Component.
    /// <summary>
    /// This component is created to define the size of hitbox collider for Monsters and NPCs.
    /// The attack check is then checked and executed inside WeaponAttackHandler.cs
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class NpcHitboxColliderAdapter : MonoBehaviour
    {
        [SerializeField] BoxCollider HitCollider;

        public void SetDimension(Bounds unityBounds)
        {
            HitCollider.center = unityBounds.center;
            HitCollider.size = unityBounds.size;
        }

        /// <summary>
        /// From now on the hitbox is the NPC's animated body (NpcBodyBounds on the NPC root) instead of the MDH's static
        /// box. Called when the body mesh is built, while the bones are still in their rest pose.
        /// </summary>
        public void FollowBody(GameObject npcRoot)
        {
            npcRoot.TryAddComponent<NpcBodyBounds>().Calibrate(this);
        }
    }
}
