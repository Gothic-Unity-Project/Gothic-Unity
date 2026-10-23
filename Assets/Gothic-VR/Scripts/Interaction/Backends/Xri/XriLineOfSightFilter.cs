using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// Target filter of the Near-Far interactors: an object can only be hovered and grabbed if nothing is between
    /// the hand and the object (no distance grab through fences, walls or chests).
    /// A ray from the hand to the closest point of the target must not hit any collider other than the target's own.
    /// Targets touching the hand are always allowed, targets beyond the max distance never (the far ray is longer, as
    /// it also points at UI).
    /// </summary>
    public class XriLineOfSightFilter : XRBaseTargetFilter
    {
        [Tooltip("Layers which block the line of sight.")]
        [SerializeField] private LayerMask _blockingLayers = 1;

        [Tooltip("Targets further away (m) can't be hovered or grabbed.")]
        [SerializeField] private float _maxDistance = 10f;

        [Tooltip("Targets closer to the hand (m) skip the check.")]
        [SerializeField] private float _minCheckDistance = 0.15f;

        [Tooltip("The ray ends this far (m) in front of the target's closest point, as the point lies on its surface.")]
        [SerializeField] private float _surfaceTolerance = 0.02f;

        private readonly RaycastHit[] _hits = new RaycastHit[16];

        public override void Process(IXRInteractor interactor, List<IXRInteractable> targets,
            List<IXRInteractable> results)
        {
            var origin = GetOrigin(interactor);
            foreach (var target in targets)
            {
                if (IsInLineOfSight(origin, target))
                    results.Add(target);
            }
        }

        private static Vector3 GetOrigin(IXRInteractor interactor)
        {
            if (interactor is NearFarInteractor nearFar && nearFar.nearInteractionCaster != null &&
                nearFar.nearInteractionCaster.effectiveCastOrigin != null)
                return nearFar.nearInteractionCaster.effectiveCastOrigin.position;

            return interactor.transform.position;
        }

        private bool IsInLineOfSight(Vector3 origin, IXRInteractable target)
        {
            if (!XriColliderUtility.TryGetClosestPoint(target, origin, out var closestPoint, out _))
                return true;

            var toTarget = closestPoint - origin;
            var distance = toTarget.magnitude;
            if (distance > _maxDistance)
                return false;

            if (distance <= _minCheckDistance)
                return true;

            var hitCount = Physics.RaycastNonAlloc(origin, toTarget / distance, _hits, distance - _surfaceTolerance,
                _blockingLayers, QueryTriggerInteraction.Ignore);

            var targetColliders = (target as XRBaseInteractable)?.colliders;
            for (var i = 0; i < hitCount; i++)
            {
                var hitCollider = _hits[i].collider;
                if (targetColliders == null || !targetColliders.Contains(hitCollider))
                    return false;
            }

            return true;
        }
    }
}
