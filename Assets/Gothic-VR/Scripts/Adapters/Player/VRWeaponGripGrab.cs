#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Gothic.VR.Interaction.Backends.Hvr;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableWeaponGripGrab: the first hand on a weapon closes around its grip. HVR's physics poser
    /// (no grab points on our items) closes the hand on the collider nearest to the palm - out of a holster, the backpack
    /// or a force grab that was the blade or the guard. Gothic's weapon meshes have their origin where NPCs hold them
    /// (ZS_RIGHTHAND), so for the moment of the grab only the collider(s) at the origin stay enabled. The second hand
    /// of a two-handed weapon grabs where it is.
    /// </summary>
    public class VRWeaponGripGrab : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;

        // The origin may lie this far outside the grip collider at most.
        private const float _maxGripDistance = 0.02f;

        private readonly List<Collider> _disabled = new();
        private readonly List<HVRHandGrabber> _hands = new();

        private void Start()
        {
            this.Inject();
            var controller = GetComponentInParent<HvrRig>();
            if (controller == null)
                return;

            foreach (var hand in new[] { controller.LeftHand, controller.RightHand })
            {
                if (hand == null)
                    continue;
                hand.BeforeGrabbed.AddListener(OnBeforeGrabbed);
                hand.Grabbed.AddListener(OnGrabbed);
                _hands.Add(hand);
            }
        }

        private void OnDestroy()
        {
            foreach (var hand in _hands)
            {
                if (hand == null)
                    continue;
                hand.BeforeGrabbed.RemoveListener(OnBeforeGrabbed);
                hand.Grabbed.RemoveListener(OnGrabbed);
            }
            RestoreColliders();
        }

        private void OnBeforeGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            RestoreColliders();
            if (!_configService.Dev.EnableWeaponGripGrab || grabbable == null || !IsWeapon(grabbable) ||
                IsHeldByOtherHand(grabbable, grabber))
                return;

            // The grip: the collider the item's origin lies deepest in (local bounds - no physics query, a held item's
            // physics pose can lag). Gothic's short sword: grip box -0.01..0.18 (depth 0.01), guard -0.04..0.00 (0).
            Collider grip = null;
            var bestDepth = -_maxGripDistance;
            var colliders = new List<Collider>();
            foreach (var collider in grabbable.Colliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;
                colliders.Add(collider);
                if (!TryGetLocalBounds(collider, grabbable.transform, out var bounds))
                    continue;
                var depth = GetDepth(bounds, Vector3.zero);
                if (depth <= bestDepth)
                    continue;
                bestDepth = depth;
                grip = collider;
            }

            // No collider at the origin (or nothing else): leave HVR alone.
            if (grip == null || colliders.Count < 2)
                return;

            foreach (var collider in colliders)
            {
                if (collider == grip)
                    continue;
                collider.enabled = false;
                _disabled.Add(collider);
            }
            Logger.Log($"[GripGrab] {grabbable.name}: grabbed at the grip (depth {bestDepth:F3} m)", LogCat.VR);
        }

        private void OnGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            // HVR posed the hand inside the grab (DynamicGrab) - the weapon is whole again.
            RestoreColliders();
        }

        private void LateUpdate()
        {
            // A cancelled grab never raises Grabbed.
            RestoreColliders();
        }

        private void RestoreColliders()
        {
            foreach (var collider in _disabled)
            {
                if (collider != null)
                    collider.enabled = true;
            }
            _disabled.Clear();
        }

        /// <summary>
        /// The collider's bounds in the item's own space (fitted boxes are convex box meshes on the item itself).
        /// </summary>
        private static bool TryGetLocalBounds(Collider collider, Transform item, out Bounds bounds)
        {
            bounds = default;
            Bounds local;
            switch (collider)
            {
                case MeshCollider { sharedMesh: not null } meshCollider:
                    local = meshCollider.sharedMesh.bounds;
                    break;
                case BoxCollider box:
                    local = new Bounds(box.center, box.size);
                    break;
                case CapsuleCollider capsule:
                    var size = Vector3.one * capsule.radius * 2f;
                    size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                    local = new Bounds(capsule.center, size);
                    break;
                default:
                    return false;
            }
            if (collider.transform == item)
            {
                bounds = local;
                return true;
            }

            // A child collider: its bounds moved into the item's space (corners).
            bounds = new Bounds(item.InverseTransformPoint(collider.transform.TransformPoint(local.center)), Vector3.zero);
            for (var i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                bounds.Encapsulate(item.InverseTransformPoint(collider.transform.TransformPoint(corner)));
            }
            return true;
        }

        /// <summary>
        /// How deep the point lies inside the bounds (smallest distance to a face) - negative outside.
        /// </summary>
        private static float GetDepth(Bounds bounds, Vector3 point)
        {
            var toMin = point - bounds.min;
            var toMax = bounds.max - point;
            return Mathf.Min(Mathf.Min(toMin.x, toMax.x), Mathf.Min(Mathf.Min(toMin.y, toMax.y), Mathf.Min(toMin.z, toMax.z)));
        }

        private static bool IsWeapon(HVRGrabbable grabbable)
        {
            // Not every grabbable is an item (a torch in a holder is a fire VOB) - the cast threw and broke its grab.
            var item = (grabbable.GetComponentInParent<VobLoader>()?.Container?.Props as VobItemProperties2)?.Instance;
            if (item == null)
                return false;
            var mainFlag = (VmGothicEnums.ItemFlags)item.MainFlag;
            return mainFlag is VmGothicEnums.ItemFlags.ItemKatNf or VmGothicEnums.ItemFlags.ItemKatFf;
        }

        private static bool IsHeldByOtherHand(HVRGrabbable grabbable, HVRGrabberBase grabber)
        {
            foreach (var other in grabbable.HandGrabbers)
            {
                if (other != null && other != grabber)
                    return true;
            }
            return false;
        }
    }
}
#endif
