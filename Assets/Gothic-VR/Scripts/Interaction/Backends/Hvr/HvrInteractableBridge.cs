#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Sockets;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Sits next to an HVRGrabbable and its VrInteractable. Forwards HVR's grabbable events synchronously to the
    /// VrInteractable and executes its commands on the HVRGrabbable.
    /// Inactive (no subscriptions) if another backend runs; prefabs carry the bridges of all backends.
    /// </summary>
    public class HvrInteractableBridge : MonoBehaviour, IVrInteractableBackend
    {
        [Inject] private readonly IVrBackend _vrBackend;

        private HVRGrabbable _grabbable;
        private VrInteractable _interactable;
        private bool _isSubscribed;

        private HVRGrabbable Grabbable
        {
            get
            {
                if (_grabbable == null)
                    _grabbable = GetComponent<HVRGrabbable>();
                return _grabbable;
            }
        }

        /// <summary>
        /// Without injection (e.g. objects outside a scene scope), HVR is assumed as before the XRI backend existed.
        /// </summary>
        public bool IsActive => _vrBackend == null || _vrBackend is HvrBackend;

        public bool IsHeld => Grabbable.IsBeingHeld;
        public bool IsSelectOnly => false;
        public IReadOnlyList<Collider> Colliders => Grabbable.Colliders;

        private void Awake()
        {
            _interactable = GetComponent<VrInteractable>();
            if (!IsActive || Grabbable == null || _interactable == null)
                return;

            Grabbable.HoverEnter.AddListener(OnHoverEnter);
            Grabbable.HoverExit.AddListener(OnHoverExit);
            Grabbable.Grabbed.AddListener(OnGrabbed);
            Grabbable.Released.AddListener(OnReleased);
            _isSubscribed = true;
        }

        private void OnDestroy()
        {
            if (!_isSubscribed || _grabbable == null)
                return;

            _grabbable.HoverEnter.RemoveListener(OnHoverEnter);
            _grabbable.HoverExit.RemoveListener(OnHoverExit);
            _grabbable.Grabbed.RemoveListener(OnGrabbed);
            _grabbable.Released.RemoveListener(OnReleased);
        }

        public void ForceRelease()
        {
            Grabbable.ForceRelease();
        }

        public void SetupColliders()
        {
            Grabbable.SetupColliders();
        }

        public void SetSocketSounds(AudioClip socketed, AudioClip unsocketed)
        {
            if (!TryGetComponent(out HVRSocketable socketable))
                return;

            socketable.SocketedClip = socketed;
            socketable.UnsocketedClip = unsocketed;
        }

        private void OnHoverEnter(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            _interactable.RaiseHoverEntered(HvrGrabArgsFactory.Create(grabber, grabbable));
        }

        private void OnHoverExit(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            _interactable.RaiseHoverExited(HvrGrabArgsFactory.Create(grabber, grabbable));
        }

        private void OnGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            _interactable.RaiseGrabbed(HvrGrabArgsFactory.Create(grabber, grabbable));
        }

        private void OnReleased(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            _interactable.RaiseReleased(HvrGrabArgsFactory.Create(grabber, grabbable));
        }
    }
}
#endif
