using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// DeveloperConfig.EnableXriSecondHandFixed: an item held with two hands, both hands equal. The line between the
    /// two grips always lies on the line between the two controllers, its middle between them - moving one hand turns
    /// the item around the other one. The turn around that line is the average of both hands. Both hands are shown on
    /// their grips (VrPhysicsHand.ShowOnGrip).
    /// Uses the tracked controllers, not the interactors' attach transforms: those are on the physical hands, which
    /// VrPhysicsHand holds at the item (the second hand's movement came out as ~0).
    /// Added at runtime by GothicGrabInteractable, used for two or more hands only (the single grab stays XRI's).
    /// </summary>
    public class GothicTwoHandGrabTransformer : XRBaseGrabTransformer
    {
        // Below this distance (m) between the grips there is no line to align.
        private const float _minGripDistance = 0.05f;

        private IXRSelectInteractor _firstHand;
        private IXRSelectInteractor _secondHand;
        // Per hand: the item's rotation relative to the controller and the grip (controller) in the item's space.
        private Quaternion _firstHoldRotation;
        private Quaternion _secondHoldRotation;
        private Vector3 _firstGripLocal;
        private Vector3 _secondGripLocal;
        private bool _isHolding;

        protected override RegistrationMode registrationMode => RegistrationMode.Multiple;

        public override void OnGrabCountChanged(XRGrabInteractable grabInteractable, Pose targetPose,
            Vector3 localScale)
        {
            HoldOn(grabInteractable, targetPose);
        }

        public override void Process(XRGrabInteractable grabInteractable,
            XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
        {
            var interactors = grabInteractable.interactorsSelecting;
            if (interactors.Count < 2)
            {
                _isHolding = false;
                return;
            }

            // Not started yet (registered after the grab count changed) or other hands: hold on where the item is.
            if (!_isHolding || interactors[0] != _firstHand || interactors[1] != _secondHand)
            {
                HoldOn(grabInteractable, targetPose);
                if (!_isHolding)
                    return;
            }

            var first = GetController(_firstHand);
            var second = GetController(_secondHand);
            if (first == null || second == null)
                return;

            // The turn around the grip line: halfway between how each hand alone would hold it.
            var rotation = Quaternion.Slerp(first.rotation * _firstHoldRotation,
                second.rotation * _secondHoldRotation, 0.5f);

            // The grip line onto the controllers' line.
            var gripLine = rotation * (_secondGripLocal - _firstGripLocal);
            var handLine = second.position - first.position;
            if (gripLine.sqrMagnitude > _minGripDistance * _minGripDistance &&
                handLine.sqrMagnitude > _minGripDistance * _minGripDistance)
                rotation = Quaternion.FromToRotation(gripLine, handLine) * rotation;

            // The middle of the grips between the controllers.
            var gripMiddle = (_firstGripLocal + _secondGripLocal) * 0.5f;
            var position = (first.position + second.position) * 0.5f - rotation * gripMiddle;

            rotation = rotation.normalized;
            if (!IsValid(rotation) || !IsFinite(position))
                return;
            targetPose = new Pose(position, rotation);
        }

        private void HoldOn(XRGrabInteractable grabInteractable, Pose targetPose)
        {
            _isHolding = false;
            var interactors = grabInteractable.interactorsSelecting;
            if (interactors.Count < 2)
                return;

            _firstHand = interactors[0];
            _secondHand = interactors[1];
            var first = GetController(_firstHand);
            var second = GetController(_secondHand);
            if (first == null || second == null)
                return;

            // From where the item is now - no jump when the second hand grabs.
            var rotation = IsValid(targetPose.rotation)
                ? targetPose.rotation.normalized
                : grabInteractable.transform.rotation;
            var inverseRotation = Quaternion.Inverse(rotation);
            _firstHoldRotation = Quaternion.Inverse(first.rotation) * rotation;
            _secondHoldRotation = Quaternion.Inverse(second.rotation) * rotation;
            _firstGripLocal = inverseRotation * (first.position - targetPose.position);
            _secondGripLocal = inverseRotation * (second.position - targetPose.position);
            _isHolding = true;
        }

        private static Transform GetController(IXRSelectInteractor interactor)
        {
            // The NearFarInteractor sits on the tracked controller.
            return interactor is Component component ? component.transform : null;
        }

        private static bool IsValid(Quaternion rotation)
        {
            var sqr = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z +
                      rotation.w * rotation.w;
            return sqr > 0.5f && !float.IsNaN(sqr) && !float.IsInfinity(sqr);
        }

        private static bool IsFinite(Vector3 position)
        {
            return !float.IsNaN(position.x + position.y + position.z) &&
                   !float.IsInfinity(position.x + position.y + position.z);
        }
    }
}
