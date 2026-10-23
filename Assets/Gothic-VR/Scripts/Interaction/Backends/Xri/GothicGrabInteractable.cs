using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// XRGrabInteractable for Gothic items. Adds what XRI lacks compared to HVR:
    /// - BeforeGrabbed: raised (via XriInteractableBridge) before XRI captures the Rigidbody state. Gothic items lie
    ///   kinematic, the game turns physics on there, so that VelocityTracking moves them and they fall when dropped.
    /// - Interpolation while held by a hand, against jitter of velocity tracked items while walking. None while
    ///   socketed, as the slot's transform moves it then.
    /// - Sockets (XriSocketBridge): while only a socket selects the item, XRI neither unparents nor moves it, as
    ///   VrSocketSlot re-parents its VobLoader. A hand grabbing it releases it from the socket first, so that the
    ///   socket restores the hierarchy before the hand's grab starts (HVR order: socket released, then grabbed).
    ///   Hands can't hover or take it while its socket's detection is off (backpack slots while the backpack is on
    ///   the shoulder, like HVR's ParentDisablesGrab).
    ///
    /// Disabled in the prefab. XriInteractableBridge enables it on the XRI backend once the runtime colliders exist.
    /// </summary>
    public class GothicGrabInteractable : XRGrabInteractable
    {
        [Tooltip("Rigidbody interpolation while held. Restored on drop.")]
        [SerializeField] private RigidbodyInterpolation _heldInterpolation = RigidbodyInterpolation.Interpolate;

        [Tooltip("While a hand holds it, other hands can neither hover nor take it (the backpack: the other hand " +
                 "takes items out of its slots).")]
        [SerializeField] private bool _isOneHandOnly;

        private XriInteractableBridge _bridge;
        private RigidbodyInterpolation _interpolationBeforeGrab;
        private bool _isInterpolationOverridden;
        private bool _isSocketDetach;

        /// <summary>
        /// Selected by a socket only (no hand).
        /// </summary>
        public bool IsSocketed => interactorsSelecting.Count == 1 && interactorsSelecting[0] is XriSocketBridge;

        protected override void Awake()
        {
            base.Awake();
            _bridge = GetComponent<XriInteractableBridge>();
        }

        /// <summary>
        /// Selected by a socket whose detection is off.
        /// </summary>
        private bool IsLockedInSocket => IsSocketed && interactorsSelecting[0] is XriSocketBridge socket &&
                                         !socket.IsDetectionEnabled;

        public override bool IsHoverableBy(IXRHoverInteractor interactor)
        {
            if (interactor is not XriSocketBridge && (IsLockedInSocket || IsHeldByOtherHand(interactor)))
                return false;

            return base.IsHoverableBy(interactor);
        }

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (interactor is not XriSocketBridge && (IsLockedInSocket || IsHeldByOtherHand(interactor)))
                return false;

            return base.IsSelectableBy(interactor);
        }

        private bool IsHeldByOtherHand(IXRInteractor interactor)
        {
            if (!_isOneHandOnly)
                return false;

            foreach (var selecting in interactorsSelecting)
            {
                if (selecting is not XriSocketBridge && !ReferenceEquals(selecting, interactor))
                    return true;
            }

            return false;
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            // The socket's VobLoader holds the item. Late is kept: it finishes a detach after the socket released it.
            if (IsSocketed && updatePhase != XRInteractionUpdateOrder.UpdatePhase.Late)
                return;

            base.ProcessInteractable(updatePhase);
        }

        protected override void OnSelectEntering(SelectEnterEventArgs args)
        {
            if (args.interactorObject is not XriSocketBridge)
                ReleaseFromSockets();

            // Before base: XRGrabInteractable.Grab() stores isKinematic, which it restores on drop.
            if (_bridge != null)
                _bridge.OnSelectEntering(args);

            base.OnSelectEntering(args);
        }

        protected override void OnSelectExiting(SelectExitEventArgs args)
        {
            _isSocketDetach = args.interactorObject is XriSocketBridge;
            base.OnSelectExiting(args);
        }

        protected override void Grab()
        {
            if (!IsSocketed)
            {
                base.Grab();
                return;
            }

            var isUnparenting = unparentTransformOnGrab;
            unparentTransformOnGrab = false;
            base.Grab();
            unparentTransformOnGrab = isUnparenting;
        }

        protected override void Detach()
        {
            // No throw out of a socket (and no warning for kinematic items).
            if (_isSocketDetach)
            {
                _isSocketDetach = false;
                return;
            }

            base.Detach();
        }

        protected override void SetupRigidbodyGrab(Rigidbody rigidbody)
        {
            // XRI calls it again when the movement type changes while held.
            if (!_isInterpolationOverridden)
            {
                _interpolationBeforeGrab = rigidbody.interpolation;
                _isInterpolationOverridden = true;
            }

            base.SetupRigidbodyGrab(rigidbody);

            // A socketed item moves with its slot's transform (holsters, backpack on the shoulder). Interpolation
            // would keep writing the Rigidbody's stale pose over it.
            rigidbody.interpolation = IsSocketed ? RigidbodyInterpolation.None : _heldInterpolation;
        }

        protected override void SetupRigidbodyDrop(Rigidbody rigidbody)
        {
            base.SetupRigidbodyDrop(rigidbody);
            if (!_isInterpolationOverridden)
                return;

            rigidbody.interpolation = _interpolationBeforeGrab;
            _isInterpolationOverridden = false;
        }

        private void ReleaseFromSockets()
        {
            if (interactionManager == null)
                return;

            for (var i = interactorsSelecting.Count - 1; i >= 0; i--)
            {
                if (interactorsSelecting[i] is XriSocketBridge socket)
                    interactionManager.SelectExit((IXRSelectInteractor)socket, (IXRSelectInteractable)this);
            }
        }
    }
}
