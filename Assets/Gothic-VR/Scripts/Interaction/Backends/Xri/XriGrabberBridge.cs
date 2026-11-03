using System;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// Sits next to the NearFarInteractor of one hand and forwards its events synchronously as IVrGrabber events.
    /// One interactor does direct and distance grabs, so Kind is always Hand. The VrGrabArgs of each event say
    /// whether the current object was grabbed directly (Hand) or pulled from a distance (Distance).
    /// BeforeGrabbed is raised by GothicGrabInteractable (XRI has no such interactor event).
    ///
    /// Physical hands (VrPhysicsHand next to the interactor): held items are attached to the physical hand, not to
    /// the controller, so they stop with the hand at a wall, and a held item touching a wall holds the hand back.
    /// A hand that got stuck while holding an item releases it.
    /// </summary>
    [RequireComponent(typeof(NearFarInteractor))]
    public class XriGrabberBridge : MonoBehaviour, IVrGrabber
    {
        [Tooltip("Added to the near caster's radius: a target this close to the hand at selection is a Hand grab, " +
                 "everything further away a Distance grab.")]
        [SerializeField]
        private float _nearGrabMargin = 0.05f;

        public event Action<VrGrabArgs> BeforeGrabbed;
        public event Action<VrGrabArgs> Grabbed;
        public event Action<VrGrabArgs> Released;

        private NearFarInteractor _interactor;
        private VrGrabberKind _heldKind = VrGrabberKind.Hand;
        private bool _isSubscribed;
        private VrPhysicsHand _physicsHand;

        public NearFarInteractor Interactor
        {
            get
            {
                if (_interactor == null)
                    _interactor = GetComponent<NearFarInteractor>();
                return _interactor;
            }
        }

        public VrGrabberKind Kind => VrGrabberKind.Hand;

        public VrHandSide HandSide => Interactor.handedness == InteractorHandedness.Left
            ? VrHandSide.Left
            : VrHandSide.Right;

        public VrInteractable Held => Interactor.hasSelection
            ? GetInteractable(Interactor.firstInteractableSelected)
            : null;

        private void Awake()
        {
            Interactor.selectEntered.AddListener(OnSelectEntered);
            Interactor.selectExited.AddListener(OnSelectExited);
            _isSubscribed = true;

            if (Interactor.farInteractionCaster is CurveInteractionCaster farCaster)
            {
                farCaster.castDistance = VRConstants.DistanceGrabMaxDistance;
                farCaster.coneCastAngle = VRConstants.DistanceGrabConeAngle;
            }
        }

        private void Start()
        {
            // Start: NearFarInteractor creates its attach controller in Awake.
            AttachToPhysicsHand();
        }

        /// <summary>
        /// The attach anchor follows a point on the physical hand instead of the interactor (the controller).
        /// </summary>
        private void AttachToPhysicsHand()
        {
            var controller = transform.parent;
            _physicsHand = controller != null ? controller.GetComponentInChildren<VrPhysicsHand>(true) : null;
            if (_physicsHand == null)
                return;

            var attachController = Interactor.interactionAttachController;
            if (attachController == null)
            {
                Logger.LogWarning($"[XRI] {name} has no interaction attach controller. Held items follow the " +
                                  "controller, not the physical hand.", LogCat.VR);
                return;
            }

            var follow = attachController.transformToFollow != null
                ? attachController.transformToFollow
                : Interactor.transform;
            attachController.transformToFollow = _physicsHand.CreateAttachPoint(follow);
            _physicsHand.HeldItemStuck += ForceRelease;
        }

        private void OnDestroy()
        {
            if (_physicsHand != null)
                _physicsHand.HeldItemStuck -= ForceRelease;

            if (!_isSubscribed || _interactor == null)
                return;

            _interactor.selectEntered.RemoveListener(OnSelectEntered);
            _interactor.selectExited.RemoveListener(OnSelectExited);
        }

        public void ForceRelease()
        {
            if (Interactor.hasSelection && Interactor.interactionManager != null)
                Interactor.interactionManager.CancelInteractorSelection((IXRSelectInteractor)Interactor);
        }

        /// <summary>
        /// Args of a selection that starts now. Remembers the kind (Hand or Distance) until the release.
        /// </summary>
        internal VrGrabArgs CreateSelectArgs(VrInteractable interactable, IXRInteractable xrInteractable)
        {
            _heldKind = ResolveKind(xrInteractable);
            return CreateArgs(interactable, _heldKind);
        }

        internal VrGrabArgs CreateHoverArgs(VrInteractable interactable, IXRInteractable xrInteractable)
        {
            return CreateArgs(interactable, ResolveKind(xrInteractable));
        }

        internal VrGrabArgs CreateHeldArgs(VrInteractable interactable)
        {
            return CreateArgs(interactable, _heldKind);
        }

        internal void RaiseBeforeGrabbed(VrGrabArgs args)
        {
            BeforeGrabbed?.Invoke(args);
        }

        private VrGrabArgs CreateArgs(VrInteractable interactable, VrGrabberKind kind)
        {
            return new VrGrabArgs(kind, HandSide, interactable, gameObject);
        }

        /// <summary>
        /// Near-Far's selectionRegion can't tell us: with InteractorFarAttachMode.Near, a far grab has no attach
        /// offset and reports Near as well. Therefore, the distance between hand and target decides.
        /// </summary>
        private VrGrabberKind ResolveKind(IXRInteractable xrInteractable)
        {
            if (xrInteractable == null || !Interactor.enableNearCasting)
                return VrGrabberKind.Distance;

            var nearRadius = Interactor.nearInteractionCaster is SphereInteractionCaster sphereCaster
                ? sphereCaster.castRadius
                : 0.1f;
            var caster = Interactor.nearInteractionCaster;
            var origin = caster != null && caster.effectiveCastOrigin != null
                ? caster.effectiveCastOrigin
                : Interactor.transform;
            var maxDistance = nearRadius + _nearGrabMargin;

            if (!XriColliderUtility.TryGetClosestPoint(xrInteractable, origin.position, out _, out var distanceSqr))
                return VrGrabberKind.Distance;

            return distanceSqr <= maxDistance * maxDistance ? VrGrabberKind.Hand : VrGrabberKind.Distance;
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            SetPhysicsHandItem(args.interactableObject);

            var interactable = GetInteractable(args.interactableObject);
            if (interactable != null)
                Grabbed?.Invoke(CreateHeldArgs(interactable));
        }

        /// <summary>
        /// Items moved by VelocityTracking. Not kinematic ones (door and chest leaves: VrHinge turns them).
        /// </summary>
        private void SetPhysicsHandItem(IXRSelectInteractable xrInteractable)
        {
            if (_physicsHand == null)
                return;

            if (xrInteractable is not XRGrabInteractable grabInteractable ||
                grabInteractable.movementType != XRBaseInteractable.MovementType.VelocityTracking ||
                !grabInteractable.TryGetComponent<Rigidbody>(out var body) || body.isKinematic)
            {
                _physicsHand.ClearHeldItem();
                return;
            }

            _physicsHand.SetHeldItem(body, grabInteractable.GetAttachTransform(Interactor),
                () => grabInteractable.GetTargetPose().rotation);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (_physicsHand != null)
                _physicsHand.ClearHeldItem();

            var interactable = GetInteractable(args.interactableObject);
            if (interactable != null)
                Released?.Invoke(CreateHeldArgs(interactable));
        }

        private static VrInteractable GetInteractable(IXRInteractable xrInteractable)
        {
            return xrInteractable is Component component ? component.GetComponent<VrInteractable>() : null;
        }
    }
}
