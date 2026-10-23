using Gothic.Core.Logging;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// XRI part of a VrSocketSlot (same GameObject). XRI's socket does the detection (trigger collider), hover and
    /// select. The Gothic part (re-parenting the VobLoader, rotation, scale, sounds, events) is done by VrSocketSlot:
    /// - Only what the slot accepts (Gothic items, or its linked interactable like the backpack), and only while a
    ///   hand holds it or right after a hand released it (items lying around next to a slot are not pulled in).
    /// - XRI must not move the grabbable child: socket snapping and the socket's scale mode are off, and
    ///   GothicGrabInteractable doesn't process (move) itself while only a socket selects it.
    ///
    /// Disabled in the prefab. Enables itself on the XRI backend only, so that no XRInteractionManager is searched
    /// or created on other backends.
    /// </summary>
    public class XriSocketBridge : XRSocketInteractor, IVrSocketBackend
    {
        [Tooltip("Seconds after a hand released an item, in which the slot still takes it.")]
        [SerializeField] private float _handReleaseWindow = 0.5f;

        [Inject] private readonly IVrBackend _vrBackend;

        private VrSocketSlot _slot;
        private XRGrabInteractable _pending;
        private bool _isDetectionEnabled = true;

        public bool IsActive => _vrBackend is XriBackend;

        /// <summary>
        /// Off: nothing is detected, and hands can't take the held interactable out (see SetDetectionEnabled).
        /// </summary>
        public bool IsDetectionEnabled => _isDetectionEnabled;

        protected override void Awake()
        {
            base.Awake();

            _slot = GetComponent<VrSocketSlot>();
            if (_slot == null)
            {
                Logger.LogError($"[XRI] {name} needs a {nameof(VrSocketSlot)} next to {nameof(XriSocketBridge)}.",
                    LogCat.VR);
                return;
            }

            // The VobLoader is moved by VrSocketSlot, not the grabbable child by XRI.
            hoverSocketSnapping = false;
            socketScaleMode = SocketScaleMode.None;
            showInteractableHoverMeshes = false;
            keepSelectedTargetValid = true;
            startingSelectedInteractable = null;

            if (IsActive)
                enabled = true;
        }

        private void Update()
        {
            TrySelectPending();
        }

        public bool TryPlace(VrInteractable interactable)
        {
            var grabInteractable = interactable != null ? interactable.GetComponent<XRGrabInteractable>() : null;
            if (grabInteractable == null || (hasSelection && !IsSelecting(grabInteractable)))
                return false;

            _pending = grabInteractable;
            TrySelectPending();
            return true;
        }

        public void ForceRelease()
        {
            _pending = null;
            if (!hasSelection || interactionManager == null)
                return;

            interactionManager.SelectExit((IXRSelectInteractor)this, firstInteractableSelected);
        }

        public void SetDetectionEnabled(bool isEnabled)
        {
            // socketActive off would also release a held item (isSelectActive is false then).
            // GothicGrabInteractable keeps hands away from the held item meanwhile (like HVR's ParentDisablesGrab).
            _isDetectionEnabled = isEnabled;
        }

        public override bool CanHover(IXRHoverInteractable interactable)
        {
            if (!_isDetectionEnabled || !base.CanHover(interactable) || _slot == null)
                return false;

            var vrInteractable = GetInteractable(interactable);
            if (vrInteractable == null || vrInteractable == _slot.Held || !_slot.Accepts(vrInteractable))
                return false;

            return IsHeldOrJustReleasedByHand(interactable);
        }

        public override bool CanSelect(IXRSelectInteractable interactable)
        {
            if (IsSelecting(interactable))
                return base.CanSelect(interactable);

            // Programmatic placement ignores the filters (IVrSocket.TryPlace).
            if (_pending != null && ReferenceEquals(interactable, _pending))
                return !hasSelection;

            if (!_isDetectionEnabled || !base.CanSelect(interactable) || _slot == null || _slot.IsOccupied)
                return false;

            var vrInteractable = GetInteractable(interactable);
            return vrInteractable != null && _slot.Accepts(vrInteractable) && IsJustReleasedByHand(interactable);
        }

        protected override bool StartSocketSnapping(XRGrabInteractable grabInteractable)
        {
            // XRI's socket grab transformer would move (and scale) the grabbable child.
            return false;
        }

        protected override void OnHoverEntered(HoverEnterEventArgs args)
        {
            base.OnHoverEntered(args);
            if (_slot != null)
                _slot.OnBackendHoverEntered(GetInteractable(args.interactableObject));
        }

        protected override void OnHoverExited(HoverExitEventArgs args)
        {
            base.OnHoverExited(args);
            if (_slot != null)
                _slot.OnBackendHoverExited(GetInteractable(args.interactableObject));
        }

        /// <summary>
        /// Before the interactable's OnSelectEntering (GothicGrabInteractable.Grab), so that it keeps the VobLoader
        /// as its parent and becomes kinematic in the slot.
        /// </summary>
        protected override void OnSelectEntering(SelectEnterEventArgs args)
        {
            if (ReferenceEquals(args.interactableObject, _pending))
                _pending = null;

            base.OnSelectEntering(args);
            if (_slot != null)
                _slot.OnBackendPlacing(GetInteractable(args.interactableObject));
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            if (_slot != null)
                _slot.OnBackendPlaced(GetInteractable(args.interactableObject));
        }

        /// <summary>
        /// After the interactable's OnSelectExiting (drop), so that VrSocketSlot restores the hierarchy last.
        /// A hand taking the item: GothicGrabInteractable releases it from the socket before the hand's grab.
        /// </summary>
        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (_slot != null)
                _slot.OnBackendRemoved(GetInteractable(args.interactableObject));
        }

        /// <summary>
        /// Finishes a programmatic placement once the interactable is registered (freshly spawned items enable their
        /// XRI interactable in Start).
        /// </summary>
        private void TrySelectPending()
        {
            // Destroyed or deactivated before it was selected: VrSocketSlot still holds it in Gothic terms.
            if (_pending == null || !_pending.gameObject.activeInHierarchy)
                return;

            if (!isActiveAndEnabled || interactionManager == null || !_pending.isActiveAndEnabled ||
                !interactionManager.IsRegistered((IXRInteractable)_pending))
                return;

            var pending = _pending;
            if (pending.isSelected)
                interactionManager.CancelInteractableSelection((IXRSelectInteractable)pending);

            interactionManager.SelectEnter((IXRSelectInteractor)this, (IXRSelectInteractable)pending);
            if (!IsSelecting(pending))
            {
                Logger.LogWarning($"[XRI] {name} couldn't select {pending.name}.", LogCat.VR);
                _pending = null;
                _slot.ForceRelease();
            }
        }

        private bool IsHeldOrJustReleasedByHand(IXRInteractable interactable)
        {
            var bridge = (interactable as Component)?.GetComponent<XriInteractableBridge>();
            return bridge != null && (bridge.IsHeldByHand || bridge.IsJustReleasedByHand(_handReleaseWindow));
        }

        private bool IsJustReleasedByHand(IXRInteractable interactable)
        {
            var bridge = (interactable as Component)?.GetComponent<XriInteractableBridge>();
            return bridge != null && !bridge.IsHeldByHand && bridge.IsJustReleasedByHand(_handReleaseWindow);
        }

        private static VrInteractable GetInteractable(IXRInteractable interactable)
        {
            return interactable is Component component ? component.GetComponent<VrInteractable>() : null;
        }
    }
}
