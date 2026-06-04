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
    }
}
