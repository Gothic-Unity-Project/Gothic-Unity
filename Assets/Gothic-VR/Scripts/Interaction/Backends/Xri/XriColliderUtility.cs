using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// Like XRInteractableUtility.TryGetClosestPointOnCollider, but without Physics.ClosestPoint on non-convex
    /// MeshColliders (mobs), which Unity doesn't support (it logs a warning on every call). Their bounds are used
    /// instead.
    /// </summary>
    public static class XriColliderUtility
    {
        public static bool TryGetClosestPoint(IXRInteractable xrInteractable, Vector3 position, out Vector3 point,
            out float distanceSqr)
        {
            point = position;
            distanceSqr = float.MaxValue;
            if (xrInteractable is not XRBaseInteractable baseInteractable)
                return false;

            foreach (var interactableCollider in baseInteractable.colliders)
            {
                if (interactableCollider == null || !interactableCollider.enabled ||
                    !interactableCollider.gameObject.activeInHierarchy)
                    continue;

                var colliderPoint = interactableCollider is MeshCollider { convex: false }
                    ? interactableCollider.ClosestPointOnBounds(position)
                    : interactableCollider.ClosestPoint(position);
                var colliderDistanceSqr = (colliderPoint - position).sqrMagnitude;
                if (colliderDistanceSqr >= distanceSqr)
                    continue;

                point = colliderPoint;
                distanceSqr = colliderDistanceSqr;
            }

            return distanceSqr < float.MaxValue;
        }
    }
}
