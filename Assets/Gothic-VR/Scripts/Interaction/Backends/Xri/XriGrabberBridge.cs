using System;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// Sits next to the NearFarInteractor of one hand and forwards its events synchronously as IVrGrabber events.
    /// One interactor does direct and distance grabs, so Kind is always Hand. The VrGrabArgs of each event say
    /// whether the current object was grabbed directly (Hand) or pulled from a distance (Distance).
    /// BeforeGrabbed is raised by GothicGrabInteractable (XRI has no such interactor event).
    /// </summary>
    [RequireComponent(typeof(NearFarInteractor))]
    public class XriGrabberBridge : MonoBehaviour, IVrGrabber
    {
        [Tooltip("Added to the near caster's radius: a target this close to the hand at selection is a Hand grab, " +
                 "everything further away a Distance grab.")]
        [SerializeField] private float _nearGrabMargin = 0.05f;

        public event Action<VrGrabArgs> BeforeGrabbed;
        public event Action<VrGrabArgs> Grabbed;
        public event Action<VrGrabArgs> Released;

        private NearFarInteractor _interactor;
        private VrGrabberKind _heldKind = VrGrabberKind.Hand;
        private bool _isSubscribed;

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
        }

        private void OnDestroy()
        {
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
            var interactable = GetInteractable(args.interactableObject);
            if (interactable != null)
                Grabbed?.Invoke(CreateHeldArgs(interactable));
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
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
