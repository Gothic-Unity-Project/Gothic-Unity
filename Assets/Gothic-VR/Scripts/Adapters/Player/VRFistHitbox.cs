using System;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.VR.Interaction.Models;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVrFists: a trigger at the knuckles of an empty VR hand, active while it makes a fist
    /// (grip + trigger). Reports NPC hitboxes it enters - VRWeaponService decides if it's a punch.
    /// </summary>
    public class VRFistHitbox : MonoBehaviour
    {
        private const float _radius = 0.07f;
        private static readonly Vector3 _knuckleOffset = new(0f, 0f, 0.06f);

        public VrHandSide HandSide { get; private set; }
        public Action<VRFistHitbox, NpcLoader, Vector3> OnNpcEntered;

        private GameObject _debugSphere;

        /// <summary>
        /// DeveloperConfig.ShowNpcColliders: the punch volume as a see-through sphere, to check where the fist hits.
        /// </summary>
        public void SetDebugVisible(bool isVisible)
        {
            if (isVisible && _debugSphere == null)
            {
                _debugSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _debugSphere.name = "FistHitboxDebug";
                Destroy(_debugSphere.GetComponent<Collider>());
                _debugSphere.transform.SetParent(transform, false);
                _debugSphere.transform.localScale = Vector3.one * (_radius * 2f);
                var debugRenderer = _debugSphere.GetComponent<Renderer>();
                debugRenderer.material.color = new Color(1f, 0.2f, 0.2f, 0.35f);
            }
            if (_debugSphere != null && _debugSphere.activeSelf != isVisible)
                _debugSphere.SetActive(isVisible);
        }

        public static VRFistHitbox Create(Transform hand, VrHandSide side)
        {
            var go = new GameObject($"FistHitbox_{side}")
            {
                layer = Constants.VobItemLayer
            };
            go.transform.SetParent(hand, false);
            go.transform.localPosition = _knuckleOffset;

            var sphere = go.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = _radius;

            // An own kinematic body - not part of the physics hand's compound collider.
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var hitbox = go.AddComponent<VRFistHitbox>();
            hitbox.HandSide = side;
            go.SetActive(false);
            return hitbox;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer != Constants.VobHitbox)
                return;

            var npcLoader = other.GetComponentInParent<NpcLoader>();
            if (npcLoader != null)
                OnNpcEntered?.Invoke(this, npcLoader, transform.position);
        }
    }
}
