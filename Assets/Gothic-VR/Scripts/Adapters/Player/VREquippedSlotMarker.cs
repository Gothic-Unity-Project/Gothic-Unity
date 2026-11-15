using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Vanilla equipped-slot background quad ("EquippedLabel.prefab"). Knows how it sits inside a slot ring, so the
    /// backpack and the NPC loot share one rule.
    /// </summary>
    public class VREquippedSlotMarker : MonoBehaviour
    {
        [Tooltip("Diameter of the quad in world space. Fills the inside of the slot ring (VR Socket Slot: ring " +
                 "radius 0.06 in the local XY plane).")]
        [SerializeField] private float _worldSize = 0.105f;

        /// <summary>
        /// Centers the quad in the parent slot, keeping its world size independent of the parent's scale.
        /// Transparent queue: the opaque item in the same plane stays in front.
        /// </summary>
        public void Place(Transform parent, Material material)
        {
            GetComponent<MeshRenderer>().sharedMaterial = material;
            transform.SetParent(parent, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            var parentScale = Mathf.Max(0.0001f, parent.lossyScale.x);
            transform.localScale = Vector3.one * (_worldSize / parentScale);
        }
    }
}
